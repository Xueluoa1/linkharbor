'use strict';
const {domainMatches,safeMedia,cli}=require('./browser-media.cjs');
const sites=['xiaohongshu.com','xhslink.com'], cdns=['xhscdn.com','xiaohongshu.com','xhsimg.com'];
function owns(raw){try{const u=new URL(raw);return ['http:','https:'].includes(u.protocol)&&domainMatches(u.hostname,sites)&&!u.username&&!u.password;}catch{return false;}}
function normalize(raw){if(!owns(raw))throw Error('请输入有效的小红书分享链接。');return raw;}
function id(raw){return owns(raw)?(new URL(raw).pathname.match(/\/(?:explore|discovery\/item)\/([a-z0-9]+)(?:\/|$)/i)?.[1]||''):'';}
const key=note=>String(note?.noteId||note?.note_id||note?.id||'');
function noteInfo(note,target,userAgent,elementSrc){
    if(!target || key(note)!==target)throw Error('返回的笔记与分享链接不一致。');
    const formats=[],seen=new Set();
    function add(values,label,attrs){
        const url=values.filter(v=>typeof v==='string').map(v=>safeMedia(v.replace(/^http:/i,'https:'),cdns)).find(Boolean);
        if(!url||seen.has(url))return;
        seen.add(url);formats.push({url,ext:'mp4',format_id:label,...attrs});
    }
    const stream=note.video?.media?.stream||{};
    for(const [codec,vcodec] of Object.entries({h264:'avc1',h265:'hev1',av1:'av01'})){
        for(const [i,f] of (stream[codec]||[]).entries()){
            if(f.has_drm||f.drm)continue;
            add([f.masterUrl||f.master_url,...(f.backupUrls||f.backup_urls||[])],codec+'-'+i,
                {width:f.width,height:f.height,tbr:(f.videoBitrate||f.video_bitrate)/1000||undefined,vcodec,acodec:'mp4a.40.2'});
        }
    }
    if(!formats.length){
        const origin=note.video?.consumer?.originVideoKey||note.video?.originVideoKey||note.video?.consumer?.origin_video_key;
        if(typeof origin==='string' && /^[\w./-]+$/.test(origin))add(['https://sns-video-bd.xhscdn.com/'+origin.replace(/^\/+/,'')],'original',{});
        add([elementSrc],'html5',{});
    }
    if(!formats.length)throw Error('此笔记未提供视频流。请确认分享的是视频笔记。');
    return {id:target,title:note.title||note.desc||('小红书笔记 '+target),uploader:note.user?.nickname,
        extractor:'xiaohongshu',extractor_key:'Xiaohongshu',webpage_url:'https://www.xiaohongshu.com/explore/'+target,formats,
        http_headers:{Referer:'https://www.xiaohongshu.com/','User-Agent':userAgent}};
}
function snapshot(target){
    const url=location.href;
    const id=target||location.pathname.match(/\/(?:explore|discovery\/item)\/([a-z0-9]+)/i)?.[1];
    const entry=window.__INITIAL_STATE__?.note?.noteDetailMap?.[id];
    const item=entry?.note?{...entry.note,noteId:entry.note.noteId||id}:null;
    const text=document.body?.innerText?.slice(0,1800)||'';
    const blocked=/当前笔记暂时无法浏览|该笔记已被删除|请登录后查看|登录后查看完整|安全验证/.test(text)||/\/(?:404|website-login|captcha)(?:\/|$)/.test(location.pathname)?
        '小红书笔记不可访问或需要登录。请粘贴带完整分享参数的链接，或在设置中绑定小红书 Cookie。':'';
    return {url,item,blocked,video:item?document.querySelector('video')?.currentSrc:null};
}
const adapter={domain:'xiaohongshu.com',normalize,owns,id,key,snapshot,info:noteInfo,
    response:s=>owns(s)&&new URL(s).pathname==='/api/sns/web/v1/feed',
    items:j=>(j?.data?.items||[]).map(i=>({...i.note_card,noteId:i.id||i.note_card?.note_id})),
    apiError:j=>j?.success===false?'小红书未返回笔记，请检查分享参数或网页登录状态。':''};
if(require.main===module)cli(adapter);
module.exports={noteInfo,normalize,id,snapshot};
