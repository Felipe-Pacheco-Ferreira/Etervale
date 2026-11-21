import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const source=await readFile(new URL('../wwwroot/main.js',import.meta.url),'utf8');
const start=source.indexOf('async function installPublicAssetCache('),end=source.indexOf('function enableCanvasPicking(',start);
assert.ok(start>=0&&end>start);
const install=new Function(source.slice(start,end)+'; return installPublicAssetCache;')();
const startCache=new Function(source.slice(start,end)+'; return startPublicAssetCache;')();
const root='https://game.example/play/',hash='A'.repeat(64),asset=root+'assets/Field/map.bmp?v='+hash;
const flush=()=>new Promise(resolve=>setImmediate(resolve));

function decorate(response,url,type='basic',redirected=false) {
    Object.defineProperties(response,{url:{value:url},type:{value:type},redirected:{value:redirected}});
    const clone=response.clone.bind(response);
    response.clone=()=>decorate(clone(),url,type,redirected);
    return response;
}
function basic(url,body='original asset',options={}) {
    return decorate(new Response(body,{status:options.status??200,headers:{'cache-control':'public, max-age=31536000, immutable',...options.headers}}),
        options.url??url,options.type??'basic',options.redirected??false);
}
async function fixture(options={}) {
    const stored=new Map(),calls=[],matches=[],puts=[],opened=[],checked=[];
    const manifest=options.manifest??`assets/Field/map.bmp\t${hash}\nContent/Fonts/UI.xnb\t${hash}\n`;
    const cache={async match(key,settings) {
        matches.push({key,settings});if(options.readError)throw options.readError;
        return stored.get(key)?.clone();
    },async put(key,response) {
        puts.push({key,response});
        if(options.putWait)await options.putWait;
        if(options.putError)throw options.putError;
        stored.set(key,response.clone());
    }};
    const original=async(input,init)=>{
        calls.push({input,init});
        const url=new URL(input?.url??input,root).href;
        if(url===root+'bootstrap.txt')return basic(url,manifest);
        if((init?.signal??input?.signal)?.aborted)throw new DOMException('Request cancelled','AbortError');
        if(options.fetchError)throw options.fetchError;
        return basic(url,options.body??'original asset',options.response);
    };
    const environment={URL,Headers,Response,ReadableStream,DOMException,fetch:original,location:{search:options.search??''}};
    if(!options.unsupported)environment.caches={async has(name){checked.push(name);return options.existing!==false;},
        async open(name){opened.push(name);if(options.openError)throw options.openError;return cache;}};
    const controller=options.useStart?await startCache(root,environment):await install(root,environment,options.fill??true);
    return {environment,original,controller,stored,calls,matches,puts,opened,checked};
}

test('all cache initialization is disabled without both explicit diagnostic opt-ins',async()=>{
    for(const search of ['', '?profile=1', '?asset-cache=1', '?profile=0&asset-cache=1', '?profile=1&asset-cache-fill=1']) {
        const f=await fixture({useStart:true,search});
        assert.equal(f.controller,null,search);assert.equal(f.environment.fetch,f.original,search);
        assert.equal(f.calls.length,0,search);assert.equal(f.checked.length,0,search);assert.equal(f.opened.length,0,search);
    }
    const reader=await fixture({useStart:true,search:'?profile=1&asset-cache=1'});
    assert.equal(reader.controller.snapshot().mode,'read-only');assert.equal(reader.controller.snapshot().writesDisabled,true);
    const fill=await fixture({useStart:true,search:'?profile=1&asset-cache=1&asset-cache-fill=1',existing:false});
    assert.equal(fill.controller.snapshot().mode,'diagnostic-fill');assert.equal(fill.controller.snapshot().writesDisabled,false);
    assert.equal(fill.opened.length,1);
});

test('normal first visit with no existing cache leaves fetch untouched and performs no manifest fetch or cache open',async()=>{
    const f=await fixture({fill:false,existing:false});
    assert.equal(f.environment.fetch,f.original);
    assert.equal(f.calls.length,0);
    assert.equal(f.opened.length,0);
    assert.deepEqual(f.checked,['noxius-public-assets-v1']);
    assert.equal(f.controller.snapshot().enabled,false);
    assert.equal(f.controller.snapshot().writesDisabled,true);
    assert.equal(await(await f.environment.fetch(asset)).text(),'original asset');
    assert.equal(f.matches.length,0);assert.equal(f.puts.length,0);
});

test('existing cache is read-only by default and misses cannot start background writes',async()=>{
    const f=await fixture({fill:false});
    f.stored.set(asset,basic(asset,'cached asset'));
    assert.equal(await(await f.environment.fetch(asset)).text(),'cached asset');
    const absent=root+'Content/Fonts/UI.xnb?v='+hash;
    assert.equal(await(await f.environment.fetch(absent)).text(),'original asset');
    await flush();
    assert.equal(f.controller.snapshot().mode,'read-only');
    assert.equal(f.controller.snapshot().hits,1);assert.equal(f.controller.snapshot().misses,1);
    assert.equal(f.puts.length,0);assert.equal(f.controller.snapshot().pendingPuts,0);
});

test('real public fetch wrapper stores a clone and returns the original response without waiting for persistence',async()=>{
    let release;const pending=new Promise(resolve=>{release=resolve;});
    const f=await fixture({putWait:pending,response:{headers:{'content-length':'14'}}});
    const response=await f.environment.fetch(asset);
    assert.equal(await response.text(),'original asset');
    assert.equal(f.controller.snapshot().pendingPuts,1);
    assert.equal(f.stored.size,0);
    release();await flush();
    const hit=await f.environment.fetch(asset);
    assert.equal(await hit.text(),'original asset');
    assert.equal(f.calls.length,2,'one manifest plus one original asset fetch');
    assert.deepEqual(f.opened,['noxius-public-assets-v1']);
    assert.equal(f.controller.snapshot().hits,1);
    assert.equal(f.controller.snapshot().hitHeaderBytes,14);
    assert.deepEqual(f.matches.at(-1).settings,{ignoreSearch:false,ignoreVary:false});
});

test('allowlist excludes private, framework, unversioned, cross-origin, ranged and conditional requests without altering their input',async()=>{
    const f=await fixture();
    f.stored.set(asset,basic(asset));
    const cases=[
        ['/api/play-session'],[root+'_framework/PristonTale.Gameplay.wasm?v='+hash],
        [root+'assets/Field/map.bmp'],[asset+'&account=private'],[asset.replace(hash,'B'.repeat(64))],
        [asset.replace('https://game.example','https://other.example')],
        [asset,{method:'POST',body:'private body'}],[asset,{headers:{Authorization:'credential'}}],
        [asset,{headers:{Range:'bytes=0-15'}}],[asset,{headers:{'If-None-Match':'"version"'}}],
        [asset,{integrity:'sha256-'+Buffer.alloc(32).toString('base64')}],
        [new Request(asset,{integrity:'sha256-'+Buffer.alloc(32).toString('base64')})],
        [asset,{cache:'no-store'}]
    ];
    for(const [input,init]of cases) {
        const before=f.calls.length;
        await f.environment.fetch(input,init);
        assert.equal(f.calls.length,before+1);
        assert.equal(f.calls.at(-1).input,input);
        assert.equal(f.calls.at(-1).init,init);
    }
    await flush();
    assert.equal(f.puts.length,0);
    assert.equal(f.matches.length,0);
});

test('manifest validates exact public paths and SHA hashes, with no prefix or traversal expansion',async()=>{
    const manifest=`assets/Field/map.bmp\t${hash}\nassets/../api/secret.bmp\t${hash}\n_framework/private.wasm\t${hash}\nassets/bad.bmp\tnot-a-hash\nhttps://other.example/assets/file.bmp\t${hash}\n`;
    const f=await fixture({manifest});
    await f.environment.fetch(asset);await flush();
    assert.equal(f.puts.length,1);
    for(const url of [root+'api/secret.bmp?v='+hash,root+'assets/bad.bmp?v='+hash,root+'_framework/private.wasm?v='+hash])await f.environment.fetch(url);
    assert.equal(f.puts.length,1);
    const conflict=await fixture({manifest:`assets/Field/map.bmp\t${hash}\nassets/Field/map.bmp\t${'B'.repeat(64)}`});
    assert.equal(conflict.environment.fetch,conflict.original);
    assert.equal(conflict.controller.snapshot().enabled,false);
});

test('only nonredirected basic 200 public responses are persisted',async()=>{
    for(const response of [{status:404},{status:206},{type:'opaque'},{redirected:true},
        {headers:{'cache-control':'no-store'}},{headers:{'cache-control':'private'}},{headers:{vary:'*'}},
        {headers:{'content-range':'bytes 0-15/200'}},{url:'https://game.example/api/login'}]) {
        const f=await fixture({response});
        await f.environment.fetch(asset);await flush();
        assert.equal(f.puts.length,0,JSON.stringify(response));
    }
});

test('unsupported storage, denied initialization, read errors and quota all fail open',async()=>{
    for(const options of [{unsupported:true},{openError:new Error('denied')}]) {
        const f=await fixture(options);
        assert.equal(f.environment.fetch,f.original);
        assert.equal(await(await f.environment.fetch(asset)).text(),'original asset');
    }
    const reads=await fixture({readError:new Error('storage unavailable')});
    await reads.environment.fetch(asset);await reads.environment.fetch(asset);
    assert.equal(reads.matches.length,1);
    assert.equal(reads.controller.snapshot().readErrors,1);
    const quota=await fixture({putError:new DOMException('full','QuotaExceededError')});
    quota.stored.set(root+'Content/Fonts/UI.xnb?v='+hash,basic(root+'Content/Fonts/UI.xnb?v='+hash,'font'));
    await quota.environment.fetch(asset);await flush();
    assert.equal(quota.controller.snapshot().quotaErrors,1);
    assert.equal(quota.controller.snapshot().writesDisabled,true);
    assert.equal(await(await quota.environment.fetch(root+'Content/Fonts/UI.xnb?v='+hash)).text(),'font');
    await quota.environment.fetch(asset);await flush();assert.equal(quota.puts.length,1);
});

test('aborts and original network errors are preserved, including an abort during a cache lookup',async()=>{
    const failure=new Error('original failure'),f=await fixture({fetchError:failure});
    await assert.rejects(f.environment.fetch(asset),error=>error===failure);
    const controller=new AbortController();controller.abort();
    await assert.rejects(f.environment.fetch(new Request(asset,{signal:controller.signal})),{name:'AbortError'});
    const aborted=await fixture();
    const pendingController=new AbortController();
    aborted.stored.set(asset,basic(asset));
    // Cache matching resolves asynchronously, so cancellation can occur before the hit is returned.
    const request=aborted.environment.fetch(asset,{signal:pendingController.signal});pendingController.abort();
    await assert.rejects(request,{name:'AbortError'});
});

test('background cache writes are bounded and cannot skip or delay any loader dependency',async()=>{
    let release;const pending=new Promise(resolve=>{release=resolve;});
    const manifest=Array.from({length:17},(_,at)=>`assets/file${at}.bmp\t${hash}`).join('\n');
    const f=await fixture({manifest,putWait:pending});
    for(let at=0;at<17;at++)assert.equal(await(await f.environment.fetch(root+`assets/file${at}.bmp?v=${hash}`)).text(),'original asset');
    assert.equal(f.controller.snapshot().pendingPuts,16);
    assert.equal(f.controller.snapshot().busyWriteSkips,1);
    assert.equal(f.calls.length,18);
    release();await flush();assert.equal(f.controller.snapshot().pendingPuts,0);
});

test('cached body reads remain abortable after headers and release the signal listener on EOF or cancellation',async()=>{
    const f=await fixture();
    const active=new Set();let cancelReason;
    const abort=new AbortController(),signal=abort.signal;
    const add=signal.addEventListener.bind(signal),remove=signal.removeEventListener.bind(signal);
    signal.addEventListener=(name,callback,options)=>{active.add(callback);add(name,callback,options);};
    signal.removeEventListener=(name,callback)=>{active.delete(callback);remove(name,callback);};
    const pending=new ReadableStream({start(controller){controller.enqueue(new Uint8Array([1,2]));},cancel(reason){cancelReason=reason;}});
    f.stored.set(asset,decorate(new Response(pending,{headers:{'cache-control':'public'}}),asset));
    const response=await f.environment.fetch(asset,{signal});
    assert.equal(response.url,asset);assert.equal(response.type,'basic');assert.equal(response.redirected,false);
    const reader=response.body.getReader();assert.deepEqual((await reader.read()).value,new Uint8Array([1,2]));
    const read=reader.read();abort.abort();
    await assert.rejects(read,{name:'AbortError'});await flush();assert.equal(active.size,0);
    // The cache's own stored branch remains available; cancellation affects only this response's read.
    assert.equal(cancelReason,undefined);
    f.stored.set(asset,basic(asset,'complete'));
    const complete=new AbortController(),tracked=new Set();
    const completeAdd=complete.signal.addEventListener.bind(complete.signal),completeRemove=complete.signal.removeEventListener.bind(complete.signal);
    complete.signal.addEventListener=(name,callback,options)=>{tracked.add(callback);completeAdd(name,callback,options);};
    complete.signal.removeEventListener=(name,callback)=>{tracked.delete(callback);completeRemove(name,callback);};
    const finished=await f.environment.fetch(asset,{signal:complete.signal});
    assert.equal(await finished.text(),'complete');assert.equal(tracked.size,0);
    const cloneSource=await f.environment.fetch(asset,{signal:new AbortController().signal});
    const clone=cloneSource.clone();assert.equal(clone.url,asset);assert.equal(clone.type,'basic');
    assert.equal(await clone.text(),'complete');assert.equal(await cloneSource.text(),'complete');
});

test('cache metrics distinguish unknown lengths and report per-map deltas without draining response bodies',async()=>{
    const f=await fixture();f.stored.set(asset,basic(asset));
    const before=f.controller.snapshot();await f.environment.fetch(asset);
    const delta=f.controller.snapshot(before);
    assert.equal(delta.hits,1);assert.equal(delta.hitsWithoutLength,1);assert.equal(delta.hitHeaderBytes,0);
    assert.equal(f.puts.length,0);
    assert.ok(source.indexOf('publicAssetCache = await cacheReady;')<source.indexOf('await host.PrepareAsync(clientAddress.href'));
    assert.match(source,/renderBundles: diagnostics\.get\('profile'\) === '1' && diagnostics\.get\('render-bundles'\) === '1'/);
    assert.ok(source.includes("diagnostics.get('profile') !== '1' || diagnostics.get('asset-cache') !== '1'"));
    assert.match(source,/gpuTiming: diagnostics\.get\('profile'\) === '1' && diagnostics\.get\('gpu-timing'\) === '1'/);
});
