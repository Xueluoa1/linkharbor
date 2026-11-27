'use strict';
const test=require('node:test'),assert=require('node:assert/strict');
const dy=require('../tools/douyin.cjs'),xhs=require('../tools/xiaohongshu.cjs');
const {importedCookies,cookieJar,configurePage,saveBilibiliCookies,resolve}=require('../tools/browser-media.cjs');
const fs=require('node:fs'),path=require('node:path'),os=require('node:os');
const {spawn,execFileSync}=require('node:child_process');
const vm=require('node:vm');
test('page requests and both media adapters use the same browser UA',async ()=>{
    for(const product of ['HeadlessChrome/158.0','Chrome/158.0']){
        const calls=[];
        const ua=await configurePage(async (method,params)=>{
            calls.push({method,params});
            if(method==='Browser.getVersion')return {userAgent:'Mozilla/5.0 '+product+' Safari/537.36'};
        });
        assert.equal(ua,'Mozilla/5.0 Chrome/158.0 Safari/537.36');
        assert.deepEqual(calls.map(c=>c.method),['Browser.getVersion','Page.enable','Network.enable','Network.setUserAgentOverride']);
        assert.deepEqual(calls.at(-1).params,{userAgent:ua,acceptLanguage:'zh-CN,zh'});
        const dyInfo=dy.downloadInfo({aweme_id:'1',video:{play_addr:{url_list:['https://v.douyinvod.com/sample']}}},'1',ua);
        const xhsInfo=xhs.noteInfo({noteId:'1',video:{consumer:{originVideoKey:'sample'}}},'1',ua);
        assert.equal(dyInfo.http_headers['User-Agent'],ua);
        assert.equal(xhsInfo.http_headers['User-Agent'],ua);
    }
});
test('Bilibili export preserves cookie scope and drops expired, unrelated and partitioned entries',t=>{
    const dir=fs.mkdtempSync(path.join(os.tmpdir(),'linkharbor-cookie-test-'));
    const file=path.join(dir,'bilibili-cookies.txt');
    t.after(()=>fs.rmSync(dir,{recursive:true,force:true}));
    const session={domain:'.bilibili.com',path:'/',name:'fixture',value:'demo',expires:-1,httpOnly:true,secure:true};
    const future=Math.floor(Date.now()/1000)+3600;
    saveBilibiliCookies([session,{...session,domain:'api.bilibili.com',expires:future,path:'/video',httpOnly:false},
        {...session,expires:0},{...session,expires:future-7200},{...session,domain:'.notbilibili.com'},
        {...session,domain:'bilibili.com.example.org'},{...session,partitionKey:{}},{...session,value:'bad\nline'}],file);
    assert.equal(fs.readFileSync(file,'utf8'),'# Netscape HTTP Cookie File\n#HttpOnly_.bilibili.com\tTRUE\t/\tTRUE\t0\tfixture\tdemo\n'
        +'api.bilibili.com\tFALSE\t/video\tTRUE\t'+future+'\tfixture\tdemo\n');
    saveBilibiliCookies([{...session,domain:'.example.org'}],file);
    assert.equal(fs.existsSync(file),false);
});

// Seed only synthetic cookies in a new disposable browser. Wait for a clean browser exit
// so the export command must reopen the profile and read the persisted cookies.
function seedFixture(exe,profile) {
    return new Promise((resolve,reject)=>{
        const child=spawn(exe,['--headless=new','--no-first-run','--no-default-browser-check','--disable-extensions',
            '--disable-background-networking','--disable-background-mode','--remote-debugging-pipe','--user-data-dir='+profile,'about:blank'],
            {windowsHide:true,stdio:['ignore','ignore','ignore','pipe','pipe']});
        let buffer='',seeded=false;
        const timer=setTimeout(()=>{child.kill();reject(Error('Fixture browser timed out'));},20000);
        function send(id,method,params={}){child.stdio[3].write(JSON.stringify({id,method,params})+'\0');}
        child.once('error',reject);
        child.stdio[3].on('error',reject);
        child.stdio[4].setEncoding('utf8').on('data',chunk=>{
            buffer+=chunk;
            let end;
            while((end=buffer.indexOf('\0'))>=0){
                const reply=JSON.parse(buffer.slice(0,end));buffer=buffer.slice(end+1);
                if(reply.id!==1)continue;
                seeded=!reply.error;
                send(2,'Browser.close');
            }
        });
        child.once('close',code=>{
            clearTimeout(timer);
            if(seeded && code===0)resolve();else reject(Error('Fixture browser failed to save test cookies'));
        });
        const entry={name:'fixture',value:'synthetic',path:'/',secure:true,httpOnly:true,expires:Math.floor(Date.now()/1000)+3600};
        send(1,'Storage.setCookies',{cookies:[{...entry,domain:'.bilibili.com'},{...entry,domain:'.example.org'}]});
    });
}
let hasBrowser=false;
for(const [name,relative] of [['chrome','Google/Chrome/Application/chrome.exe'],['edge','Microsoft/Edge/Application/msedge.exe']]){
    const exe=[process.env.ProgramFiles,process.env['ProgramFiles(x86)'],process.env.LOCALAPPDATA]
        .filter(Boolean).map(root=>path.join(root,relative)).find(file=>fs.existsSync(file));
    hasBrowser ||= Boolean(exe);
    test(name+' reopens its Bilibili profile and exports only fixture site cookies',{skip:exe?false:'browser not installed'},async t=>{
        const app=fs.mkdtempSync(path.join(os.tmpdir(),'linkharbor-browser-test-'));
        t.after(()=>fs.promises.rm(app,{recursive:true,force:true,maxRetries:12,retryDelay:250}));
        const root=path.join(app,'data','browser-profile'),profile=path.join(root,'bilibili',name);
        const file=path.join(root,'bilibili-cookies.txt'),script=path.join(app,'tools','browser-media.cjs');
        fs.mkdirSync(path.dirname(script),{recursive:true});fs.copyFileSync(require.resolve('../tools/browser-media.cjs'),script);
        fs.mkdirSync(profile,{recursive:true});
        const run=(dir,domain)=>execFileSync(process.execPath,[script,'--export-cookies',exe,dir,domain,file],
            {windowsHide:true,stdio:'pipe',timeout:30000});
        fs.writeFileSync(file,'obsolete fixture');
        run(profile,'.bilibili.com');
        assert.equal(fs.existsSync(file),false);
        await seedFixture(exe,profile);
        assert.equal(run(profile,'.bilibili.com').length,0);
        const rows=fs.readFileSync(file,'utf8').trimEnd().split('\n');
        assert.deepEqual(rows.slice(0,1),['# Netscape HTTP Cookie File']);
        assert.equal(rows.length,2);
        assert.match(rows[1],/^#HttpOnly_\.bilibili\.com\tTRUE\t\/\tTRUE\t\d+\tfixture\tsynthetic$/);
        assert.throws(()=>run(profile,'example.org'),/仅支持/);
        assert.throws(()=>run(root,'.bilibili.com'),/仅支持/);
    });
}
test('first page navigation sends the language and UA used for the media download',{skip:!hasBrowser},async t=>{
    const root=fs.mkdtempSync(path.join(os.tmpdir(),'linkharbor-header-test-'));
    t.after(()=>fs.promises.rm(root,{recursive:true,force:true,maxRetries:12,retryDelay:250}));
    let headers;
    const server=require('node:http').createServer((req,res)=>{
        if(req.url==='/sample')headers=req.headers;
        res.writeHead(200,{'Content-Type':'text/html'});res.end('<title>fixture</title>');
    });
    await new Promise((done,fail)=>server.once('error',fail).listen(0,'127.0.0.1',done));
    t.after(()=>new Promise(done=>{server.close(done);server.closeAllConnections();}));
    const url='http://127.0.0.1:'+server.address().port+'/sample';
    const site={normalize:s=>s,id:()=> 'sample',owns:s=>s===url,key:item=>item.id,response:()=>false,
        snapshot:()=>({url:location.href,item:{id:'sample'}}),
        info:(item,id,ua)=>({formats:[{url}],http_headers:{'User-Agent':ua}})};
    const info=await resolve(site,url,root);
    assert.match(headers['user-agent'],/Chrome\//);
    assert.doesNotMatch(headers['user-agent'],/HeadlessChrome/);
    assert.equal(headers['accept-language'],'zh-CN,zh;q=0.9');
    assert.equal(info.http_headers['User-Agent'],headers['user-agent']);
});
test('page hydration selects the requested work, detects verification and preserves the note token',()=>{
    const target='7710888906350251264',item={aweme_id:target,video:{width:720}};
    const context={location:new URL('https://www.douyin.com/video/'+target),self:{},window:{},
        document:{title:'',body:{innerText:''},getElementById:()=>null}};
    function inspect(){return vm.runInNewContext('('+dy.snapshot.toString()+')('+JSON.stringify(target)+')',context);}
    context.window._ROUTER_DATA={related:{aweme_id:'other',video:{}},detail:item};
    assert.equal(inspect().item.aweme_id,target);
    delete context.window._ROUTER_DATA;
    context.self.__pace_f=[[1,'12:'+JSON.stringify({detail:item})+'\n13:ignored']];
    assert.equal(inspect().item.aweme_id,target);
    context.self.__pace_f=[];context.document.title='安全验证';assert.match(inspect().blocked,/验证/);
    const url='https://www.xiaohongshu.com/explore/abc?xsec_token=x%2Bz';
    const snapshot=vm.runInNewContext('('+xhs.snapshot.toString()+')("abc")',{location:new URL(url),
        window:{__INITIAL_STATE__:{note:{noteDetailMap:{abc:{note:{title:'sample'}}}}}},document:{body:{innerText:''},querySelector:()=>null}});
    assert.equal(snapshot.item.noteId,'abc');assert.equal(snapshot.url,url);
});
test('Douyin deduplicates authorized playback qualities and maps codecs',()=>{
    const addr=h=>({height:h,width:1280,url_list:['https://v.douyinvod.com/'+h]});
    const video={bit_rate:[{bit_rate:900000,is_h265:1,play_addr:addr(1080)},{bit_rate:450000,play_addr:addr(720)}],play_addr:addr(720)};
    const info=dy.downloadInfo({aweme_id:'123',video_control:{allow_download:false},video},'123','testUA');
    assert.deepEqual(info.formats.map(f=>[f.height,f.vcodec,f.tbr]),[[720,'avc1',450],[1080,'hev1',900]]);
    assert.deepEqual(info.http_headers,{Referer:'https://www.douyin.com/','User-Agent':'testUA'});
    assert.throws(()=>dy.downloadInfo({aweme_id:'123',images:[{}]},'123','ua'),/静态图文/);
    assert.equal(dy.downloadInfo({aweme_id:'123',images:[{}],music:{play_url:addr(1)}},'123','ua').formats[0].vcodec,'none');
});
test('Netscape import and download cookies respect domain scope',()=>{
    const raw='#HttpOnly_.douyin.com\tTRUE\t/video\tTRUE\t0\tfixture\tvalue\n.evil.test\tTRUE\t/\tFALSE\t0\tnoise\t1';
    const list=importedCookies(raw,'douyin.com');
    assert.equal(list.length,1);assert.ok(list[0].httpOnly);
    assert.equal(cookieJar(list,['https://cdn.douyinvod.com/720']),'');
    assert.equal(cookieJar(list,['https://www.douyin.com/video/1']),raw.split('\n')[0]);
});
test('Xiaohongshu keeps query and identifies stream alternatives',()=>{
    const url='https://www.xiaohongshu.com/explore/abc?xsec_token=x%2Fz&xsec_source=share';
    assert.equal(xhs.normalize(url),url);assert.equal(xhs.id(url),'abc');
    const note={noteId:'abc',video:{media:{stream:{h264:[{masterUrl:'https://x.xhscdn.com/1',width:640}],
        h265:[{backupUrls:['https://x.xhscdn.com/2']}],av1:[{masterUrl:'https://x.xhscdn.com/3'}]}}}};
    assert.deepEqual(xhs.noteInfo(note,'abc','UA').formats.map(f=>f.vcodec),['avc1','hev1','av01']);
    assert.throws(()=>xhs.noteInfo(note,'def','UA'),/不一致/);
    assert.throws(()=>xhs.noteInfo({noteId:'abc'},'abc','UA','https://other.example/1'),/未提供/);
    assert.equal(xhs.noteInfo({noteId:'abc',video:{consumer:{originVideoKey:'a/b'}}},'abc','UA').formats[0].url,'https://sns-video-bd.xhscdn.com/a/b');
});
