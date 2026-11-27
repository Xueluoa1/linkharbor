'use strict';
const {domainMatches,safeMedia,cli}=require('./browser-media.cjs');
const sites=['douyin.com','iesdouyin.com'];
const cdns=[...sites,'douyinvod.com','douyinpic.com','bytecdn.cn','zjcdn.com','byteimg.com','amemv.com','snssdk.com'];
function owns(raw){try{const u=new URL(raw);return ['https:','http:'].includes(u.protocol)&&domainMatches(u.hostname,sites)&&!u.username&&!u.password;}catch{return false;}}
function id(raw){
    if(!owns(raw))return '';
    const u=new URL(raw);
    const value=u.searchParams.get('modal_id')||u.pathname.match(/\/(?:video|note|slides)\/(\d+)(?:\/|$)/)?.[1]||'';
    return /^\d{1,20}$/.test(value)?value:'';
}
function normalize(raw){
    if(/^\d{1,20}$/.test(raw))return 'https://www.douyin.com/video/'+raw;
    if(!owns(raw))throw Error('请输入有效的抖音作品链接。');
    if(new URL(raw).searchParams.has('modal_id') && id(raw))return 'https://www.douyin.com/video/'+id(raw);
    return raw;
}
const key=w=>String(w?.aweme_id??w?.awemeId??'');
function downloadInfo(work,target,userAgent){
    if(!target || key(work)!==target)throw Error('返回的抖音作品与链接不一致。');
    if(work.status?.is_delete || work.status?.is_prohibited || work.status?.private_status>0)throw Error('作品已删除或访问受限。');
    const formats=[],seen=new Set(), video=work.video||{};
    function add(addr,label,attrs={}){
        const urls=Array.isArray(addr)?addr:(addr?.url_list??addr?.urlList??(typeof addr==='string'?[addr]:[]));
        const url=urls.map(u=>safeMedia(u,cdns)).find(Boolean);
        if(!url||seen.has(url))return;
        seen.add(url);
        formats.push({format_id:label,url,ext:'mp4',width:addr.width||video.width||undefined,height:addr.height||video.height||undefined,...attrs});
    }
    for(const [index,rate] of (video.bit_rate||video.bitRateList||[]).entries()){
        if(rate.is_drm||rate.isDrm)continue;
        add(rate.play_addr||rate.playAddr,'video-'+index,{tbr:(rate.bit_rate||rate.bitRate)/1000||undefined,
            vcodec:rate.is_h265===1||rate.is_bytevc1===1||rate.isH265?'hev1':'avc1',acodec:'mp4a.40.2'});
    }
    add(video.play_addr||video.playAddr,'play');
    add(video.download_addr||video.downloadAddr,'download');
    if(!formats.length && work.images?.length){
        for(const [i,img] of work.images.entries())add(img.video?.play_addr||img.video?.playAddr,'live-'+i);
        if(!formats.length)add(work.music?.play_url||work.music?.playUrl,'music',{ext:'m4a',vcodec:'none'});
        if(!formats.length)throw Error('这是静态图文作品，未提供实况视频或音轨。');
    }
    if(!formats.length)throw Error('该作品未提供可用的视频或音频流。');
    formats.sort((a,b)=>(a.height||0)-(b.height||0)||(a.tbr||0)-(b.tbr||0));
    return {id:target,title:work.desc||('抖音作品 '+target),uploader:work.author?.nickname,duration:video.duration/1000||undefined,
        webpage_url:'https://www.douyin.com/video/'+target,extractor:'douyin',extractor_key:'Douyin',formats,
        http_headers:{Referer:'https://www.douyin.com/','User-Agent':userAgent}};
}
// Runs in the work page. Parse JSON only; streamed hydration payloads are not executable input.
function snapshot(target){
    const url=location.href;
    const id=target||location.pathname.match(/\/(?:video|note|slides)\/(\d+)/)?.[1];
    function find(root){
        const queue=[root],seen=new Set();
        for(let i=0;i<queue.length && i<12000;i++){
            let item=queue[i];
            if(typeof item==='string'){
                if(item.length>5e6||!item.includes(id))continue;
                try{item=JSON.parse(item)}catch{
                    if(item.includes('\n')){queue.push(...item.split('\n').slice(0,2000));continue;}
                    const brace=item.indexOf('{');
                    if(brace<0)continue;
                    try{item=JSON.parse(item.slice(brace))}catch{continue}
                }
            }
            if(!item||typeof item!=='object'||seen.has(item))continue;
            seen.add(item);
            if(String(item.aweme_id??item.awemeId)===id && (item.video||item.images))return item;
            for(const v of Object.values(item))if(v && (typeof v==='object'||typeof v==='string'&&v.includes(id)))queue.push(v);
        }
    }
    let render=document.getElementById('RENDER_DATA')?.textContent;
    if(render?.startsWith('%'))try{render=decodeURIComponent(render)}catch{}
    const item=id && (find(render)||find(window._ROUTER_DATA)||find(self.__pace_f));
    const text=document.body?.innerText?.slice(0,1500)||'';
    const blocked=/验证码|安全验证|请完成验证/.test(document.title)||/请完成下列验证|拖动滑块完成验证/.test(text)?
        '触发抖音网页验证，请在设置中绑定抖音 Cookie 或使用浏览器登录态后重试。':
        /作品已删除|当前作品不可见|登录后才能观看/.test(text)?'作品不可访问或需要登录。':'';
    return {url,item,blocked};
}
const adapter={domain:'douyin.com',normalize,owns,id,key,snapshot,info:downloadInfo,
    response:s=>{try{return owns(s)&&new URL(s).pathname==='/aweme/v1/web/aweme/detail/'}catch{return false}},
    items:j=>[j?.aweme_detail],apiError:j=>String(j?.status_msg||j?.filter_detail?.detail_msg||'').slice(0,180)};
if(require.main===module)cli(adapter);
module.exports={downloadInfo,id,normalize,snapshot};
