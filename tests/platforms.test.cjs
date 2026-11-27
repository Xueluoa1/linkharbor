'use strict';
const test=require('node:test'),assert=require('node:assert/strict');
const dy=require('../tools/douyin.cjs'),xhs=require('../tools/xiaohongshu.cjs');
const {importedCookies,cookieJar}=require('../tools/browser-media.cjs');
const vm=require('node:vm');
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
