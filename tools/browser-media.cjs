'use strict';
const fs = require('node:fs');
const path = require('node:path');
const {spawn} = require('node:child_process');
const {setTimeout: delay} = require('node:timers/promises');

function domainMatches(host, domains) {
    return domains.some(d => host === d || host.endsWith('.'+d));
}
function safeMedia(value, domains) {
    try {
        const u = new URL(value);
        return u.protocol === 'https:' && !u.username && !u.password && domainMatches(u.hostname,domains) ? u.href : null;
    } catch { return null; }
}
function importedCookies(text, site) {
    const cookies = [];
    for (let line of text.replace(/^\uFEFF/,'').split(/\r?\n/)) {
        const httpOnly=line.startsWith('#HttpOnly_');
        if (httpOnly) line=line.slice(10);
        if (!line || line.startsWith('#')) continue;
        const [domain,subdomains,pathname,secure,expiry,name,value]=line.split('\t');
        if (!domain || !domainMatches(domain.replace(/^\./,''),[site]) || !name || value===undefined ||
            !pathname?.startsWith('/') || /[\r\n\0]/.test(name+value)) continue;
        const host=domain.replace(/^\./,'');
        const c={name,value,path:pathname,httpOnly,secure:secure==='TRUE',url:'https://'+host+pathname};
        if (subdomains==='TRUE') c.domain='.'+host;
        if (Number(expiry)>0) {
            if (Number(expiry)<=Date.now()/1000) continue;
            c.expires=Number(expiry);
        }
        cookies.push(c);
    }
    return cookies;
}
function cookieJar(cookies, urls) {
    const hosts=urls.map(s=>new URL(s).hostname);
    return cookieRows(cookies.filter(c=>hosts.some(h=>c.domain.startsWith('.')?
        domainMatches(h,[c.domain.slice(1)]):h===c.domain)));
}
function cookieRows(cookies) {
    const now=Date.now()/1000;
    return cookies.filter(c=>!c.partitionKey && !c.partitionKeyOpaque && (c.expires===undefined || c.expires===-1 || c.expires>now)
        && !/[\0\r\n\t]/.test(c.domain+c.path+c.name+c.value))
        .map(c=>[(c.httpOnly?'#HttpOnly_':'')+c.domain,c.domain.startsWith('.')?'TRUE':'FALSE',
            c.path,c.secure?'TRUE':'FALSE',Math.max(0,Math.floor(c.expires||0)),c.name,c.value].join('\t')).join('\n');
}
function saveBilibiliCookies(cookies, outFile) {
    const rows=cookieRows(cookies.filter(c=>domainMatches(c.domain.replace(/^\./,''),['bilibili.com'])));
    if(!rows) { fs.rmSync(outFile,{force:true}); return; }
    const temp=outFile+'.'+require('node:crypto').randomUUID()+'.tmp';
    try {
        fs.writeFileSync(temp,'# Netscape HTTP Cookie File\n'+rows+'\n',{encoding:'utf8',flag:'wx',mode:0o600});
        fs.renameSync(temp,outFile);
    } finally { fs.rmSync(temp,{force:true}); }
}
function browserPaths() {
    const paths=[];
    for(const [name,relative] of [['chrome','Google/Chrome/Application/chrome.exe'],['edge','Microsoft/Edge/Application/msedge.exe']]) {
        for(const base of [process.env.ProgramFiles,process.env['ProgramFiles(x86)'],process.env.LOCALAPPDATA].filter(Boolean)) {
            const exe=path.join(base,relative);
            if(fs.existsSync(exe)){paths.push({name,exe});break;}
        }
    }
    if(!paths.length) throw Error('网页解析需要 Microsoft Edge 或 Google Chrome。');
    return paths;
}
async function resolve(adapter,input,root,proxy='',cookieFile='') {
    const url=adapter.normalize(input);
    fs.mkdirSync(root,{recursive:true});
    const deadline=Date.now()+19000;
    let reason;
    for(const browser of browserPaths()) {
        // Imported cookies belong to this run; clearing a binding must not leave them in the login profile.
        const profile=cookieFile?fs.mkdtempSync(path.join(root,'import-')):path.join(root,browser.name);
        try {
            fs.mkdirSync(profile,{recursive:true});
            return await visit(adapter,url,browser.exe,profile,proxy,cookieFile,deadline);
        } catch(error) {
            if(!error.startup || Date.now()>=deadline) throw error;
            reason=error;
        } finally {
            if(cookieFile) fs.rmSync(profile,{recursive:true,force:true,maxRetries:4,retryDelay:100});
        }
    }
    throw reason;
}
async function withBrowser(exe,profile,proxy,deadline,inspect) {
    // Use the debugging pipe inherited by this process. No listening port, stale port file or attachment to another window.
    const flags=['--headless=new','--no-first-run','--no-default-browser-check','--disable-extensions','--disable-background-mode',
        '--remote-debugging-pipe','--autoplay-policy=no-user-gesture-required','--mute-audio','--window-size=1280,800',
        '--user-data-dir='+profile,proxy?'--proxy-server='+proxy:'--no-proxy-server','about:blank'];
    const child=spawn(exe,flags,{stdio:['ignore','ignore','ignore','pipe','pipe'],windowsHide:true});
    const pending=new Map();
    let sequence=0,sessionId,buffer=Buffer.alloc(0),startup=true,closed=false,finished=false,onEvent;
    let fail;
    const interrupted=new Promise((_,reject)=>{fail=reject;});
    const timer=setTimeout(()=>fail(Error(startup?'浏览器未就绪，请先关闭拾链的授权窗口。':
        '浏览器操作超时，请检查网络或重新打开网页登录授权。')),Math.max(1,deadline-Date.now()));
    const startTimer=setTimeout(()=>{if(startup)fail(Error('初始化浏览器网络监听超时。'))},6000);
    function command(method,params={},useSession=true) {
        if(closed) return Promise.reject(Error('网页解析浏览器已关闭。'));
        const id=++sequence;
        return new Promise((resolve,reject)=>{
            pending.set(id,{resolve,reject});
            child.stdio[3].write(JSON.stringify({id,method,params,sessionId:useSession?sessionId:undefined})+'\0');
        });
    }
    function disconnected() {
        closed=true;
        const error=Error('独立浏览器已关闭或正在其他窗口使用，请关闭授权窗口后重试。');
        fail(error);
        for (const call of pending.values()) call.reject(error);
        pending.clear();
    }
    child.once('exit',disconnected);child.once('error',disconnected);
    child.stdio[3].on('error',disconnected);child.stdio[4].on('error',disconnected);
    async function event(m) {
        if(m.id) {
            const call=pending.get(m.id);
            if(call){pending.delete(m.id);if(m.error)call.reject(Error(m.error.message));else call.resolve(m.result);}
            return;
        }
        if(onEvent && sessionId && m.sessionId===sessionId && !finished) await onEvent(m);
    }
    child.stdio[4].on('data',chunk=>{
        buffer=Buffer.concat([buffer,chunk]);
        let end;
        while((end=buffer.indexOf(0))>=0){
            const frame=buffer.subarray(0,end);buffer=buffer.subarray(end+1);
            if(frame.length)try{event(JSON.parse(frame)).catch(fail)}catch(error){fail(error)}
        }
    });
    async function start(){
        const {targetId}=await command('Target.createTarget',{url:'about:blank'},false);
        sessionId=(await command('Target.attachToTarget',{targetId,flatten:true},false)).sessionId;
        startup=false;
        return inspect(command,fn=>{onEvent=fn;},()=>!finished && !closed);
    }
    try {return await Promise.race([start(),interrupted]);}
    catch(e){e.startup=startup;throw e;}
    finally{
        finished=true;clearTimeout(timer);clearTimeout(startTimer);
        if(!closed){command('Browser.close',{},false).catch(()=>{});await Promise.race([new Promise(r=>child.once('exit',r)),delay(2000,undefined,{ref:false})]);}
        if(!closed)child.kill();
        child.stdio[3].destroy();child.stdio[4].destroy();
    }
}
async function configurePage(command) {
    const rawUa=(await command('Browser.getVersion',{},false)).userAgent;
    const ua=rawUa.replace(/HeadlessChrome\//g,'Chrome/');
    await command('Page.enable');
    await command('Network.enable');
    // Chromium adds the HTTP quality weights to this ordered language list.
    await command('Network.setUserAgentOverride',{userAgent:ua,acceptLanguage:'zh-CN,zh'});
    return ua;
}
async function visit(site,url,exe,profile,proxy,cookies,deadline) {
    return withBrowser(exe,profile,proxy,deadline,async function inspect(command,listen,active){
        const responses=new Set(), candidates=new Map();
        let currentId=site.id(url),firstFailure='';
        listen(async ({method,params:p})=>{
            if(method==='Page.frameNavigated' && !p.frame.parentId && !currentId) currentId=site.id(p.frame.url);
            if(method==='Network.responseReceived' && site.response(p.response.url)) {
                if(p.response.status===200) responses.add(p.requestId);
                else firstFailure='作品详情接口返回 HTTP '+p.response.status+'。';
            }
            if(method==='Network.loadingFinished' && responses.delete(p.requestId)){
                let json;
                try {const r=await command('Network.getResponseBody',{requestId:p.requestId});
                    json=JSON.parse(r.base64Encoded?Buffer.from(r.body,'base64').toString('utf8'):r.body);
                } catch {return;}
                for(const item of site.items(json)) if(item && site.key(item)) candidates.set(site.key(item),item);
                if(!candidates.size) firstFailure=site.apiError(json)||firstFailure;
            }
        });
        const ua=await configurePage(command);
        if(cookies && fs.existsSync(cookies)) {
            const list=importedCookies(fs.readFileSync(cookies,'utf8'),site.domain);
            if(list.length) await command('Network.setCookies',{cookies:list});
        }
        const nav=await command('Page.navigate',{url});
        if(nav.errorText && nav.errorText!=='net::ERR_ABORTED')throw Error('打开作品页失败：'+nav.errorText);
        const started=Date.now();
        while(active()) {
            let snap;
            try{
                const expression='('+site.snapshot.toString()+')('+JSON.stringify(currentId)+')';
                snap=(await command('Runtime.evaluate',{expression,returnByValue:true})).result.value;
            }catch{ /* Redirects replace the JavaScript context. */ }
            if(snap && site.owns(snap.url)){
                currentId=currentId||site.id(snap.url);
                const candidate=candidates.get(currentId) || (snap.item && site.key(snap.item)===currentId?snap.item:null);
                if(candidate) {
                    const info=site.info(candidate,currentId,ua,snap.video);
                    const urls=info.formats.map(f=>f.url);
                    const jar=cookieJar((await command('Network.getCookies',{urls})).cookies,urls);
                    if(jar)info._linkharbor_cookie_jar=jar;
                    return info;
                }
                if(Date.now()-started>=2500 && (snap.blocked || firstFailure))throw Error(snap.blocked||firstFailure);
            }
            await delay(450);
        }
    });
}
async function exportCookies(exe,profile,domain,outFile) {
    const app=fs.realpathSync.native(path.resolve(__dirname,'..'));
    const installed=fs.existsSync(path.join(app,'installed-mode'));
    const root=installed ? path.join(fs.realpathSync.native(process.env.LOCALAPPDATA),'LinkHarbor','browser-profile')
        : path.join(app,'data','browser-profile');
    const browser=browserPaths().find(b=>path.relative(b.exe,exe)==='');
    if(!browser || !['.bilibili.com','bilibili.com'].includes(domain)
        || path.relative(path.join(root,'bilibili',browser.name),fs.realpathSync.native(profile))!==''
        || path.relative(root,fs.realpathSync.native(path.dirname(outFile)))!=='' || path.basename(outFile)!=='bilibili-cookies.txt')
        throw Error('仅支持拾链专用的 B 站授权目录与文件。');
    const cookies=await withBrowser(exe,profile,'',Date.now()+20000,async command=>
        (await command('Storage.getCookies',{},false)).cookies);
    saveBilibiliCookies(cookies,outFile);
}
function cli(adapter){
    resolve(adapter,...process.argv.slice(2)).then(info=>console.log(JSON.stringify(info))).catch(e=>{console.error(e.message);process.exitCode=1});
}
if(require.main===module){
    const [mode,...args]=process.argv.slice(2);
    if(mode==='--export-cookies' && args.length===4)
        exportCookies(...args).catch(e=>{console.error(e.message);process.exitCode=1;});
    else {console.error('缺少 B 站网页登录授权参数。');process.exitCode=1;}
}
module.exports={domainMatches,safeMedia,importedCookies,cookieJar,configurePage,saveBilibiliCookies,resolve,cli};
