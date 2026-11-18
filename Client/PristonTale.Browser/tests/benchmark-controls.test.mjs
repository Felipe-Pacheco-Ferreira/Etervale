import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const source=await readFile(new URL('../wwwroot/main.js',import.meta.url),'utf8');
const start=source.indexOf('function enableTextureArrayBenchmark('),end=source.indexOf('showLoading(text.loadingClient);',start);
assert.ok(start>=0&&end>start);
function fixture({arrays=true,bundles=true,supported=true,profile=true,gpu=false,worker=false,zeroLights=false,internalPost=false}={}) {
    const elements=[],panels=[],calls=[],logs=[];
    const document={createElement(tag){const element={tag,style:{},events:{},children:[],attributes:{},
        append(...children){this.children.push(...children);},setAttribute(name,value){this.attributes[name]=value;},
        addEventListener(name,handler){this.events[name]=handler;}};elements.push(element);return element;}};
    const census={textureArrays:{outputObjects:4,baselineObjects:8},renderBundles:{requested:bundles,supported,enabled:false,chunks:3},
        gpu:gpu?{requested:true,supported,mode:'sampled'}:null,zeroPointLights:zeroLights?{requested:true,enabled:true}:null,internalPost:internalPost?{requested:true,enabled:true,width:960,height:540}:null};
    const graphics={textureArraysRequested:arrays,textureArrayLayers:128,textureArraysEnabled:false,workerRendering:worker,
        getProfile(){return census;},requestProfile(){calls.push(['profile']);return Promise.resolve(census);},
        setTextureArraysEnabled(value){const apply=()=>{calls.push(['arrays',value]);this.textureArraysEnabled=value;};return worker?Promise.resolve().then(apply):apply();},
        setRenderBundlesEnabled(value){calls.push(['bundles',value]);census.renderBundles.enabled=value;},
        setGpuTimingMode(mode){calls.push(['gpu',mode]);census.gpu.mode=mode;return mode;}};
    graphics.setZeroPointLightsEnabled=value=>{
        const apply=()=>{calls.push(['zeroLights',value]);census.zeroPointLights.enabled=value;};
        return worker?Promise.resolve().then(apply):apply();
    };
    graphics.setInternalPostEnabled=value=>{
        const apply=()=>{calls.push(['internalPost',value]);census.internalPost.enabled=value;};
        return worker?Promise.resolve().then(apply):apply();
    };
    const initialize=new Function('document','root','console',source.slice(start,end)+';return enableTextureArrayBenchmark;')(
        document,{append(panel){panels.push(panel);}},{info(line){logs.push(line);}});
    let resets=0;
    initialize(graphics,()=>{resets++;return census;},profile);
    return {elements,panels,calls,logs,get resets(){return resets;},button(label){return elements.find(element=>element.textContent===label);}};
}
test('real diagnostic A/B controls toggle arrays and bundles independently and reset measured profiles',()=>{
    const f=fixture();
    f.button('Com agrupamento').events.click();f.button('Com pacotes').events.click();f.button('Sem pacotes').events.click();
    assert.deepEqual(f.calls,[['arrays',true],['bundles',true],['bundles',false]]);
    assert.equal(f.resets,3);
    assert.equal(f.button('Com agrupamento').attributes['aria-pressed'],'true');
    assert.equal(f.button('Sem pacotes').attributes['aria-pressed'],'true');
    assert.ok(f.logs.some(line=>line.startsWith('[RenderBundleBenchmark]')));
});
test('diagnostic bundle controls work without the arrays flag and disable unsupported encoding',()=>{
    const f=fixture({arrays:false,supported:false});
    assert.equal(f.button('Com agrupamento'),undefined);
    assert.equal(f.button('Com pacotes').disabled,true);
    assert.equal(f.panels.length,1);
    assert.equal(fixture({arrays:false,bundles:false}).panels.length,0);
});

test('worker A/B controls wait for the applied state before resetting the measured profile window',async()=>{
    const f=fixture({worker:true});
    const changed=f.button('Com agrupamento').events.click();
    assert.equal(f.resets,0);
    assert.equal(f.button('Com agrupamento').attributes['aria-pressed'],'false');
    await changed;
    assert.deepEqual(f.calls,[['arrays',true],['profile']]);
    assert.equal(f.resets,1);
    assert.equal(f.button('Com agrupamento').attributes['aria-pressed'],'true');
});

test('normal visits do not create diagnostic controls even when batching is requested',()=>{
    const f=fixture({arrays:true,bundles:true,profile:false});
    assert.equal(f.panels.length,0);assert.equal(f.elements.length,0);assert.equal(f.resets,0);
});

test('same-session point-light controls are diagnostic-only and await worker application before resetting profiles',async()=>{
    const f=fixture({arrays:false,bundles:false,worker:true,zeroLights:true});
    assert.equal(f.panels.length,1);
    assert.equal(f.button('Luzes otimizadas').attributes['aria-pressed'],'true');
    const disabled=f.button('Luzes originais').events.click();
    assert.equal(f.resets,0);
    await disabled;
    assert.equal(f.button('Luzes originais').attributes['aria-pressed'],'true');
    await f.button('Luzes otimizadas').events.click();
    assert.deepEqual(f.calls,[['zeroLights',false],['profile'],['zeroLights',true],['profile']]);
    assert.equal(f.resets,2);
    assert.equal(f.button('Luzes otimizadas').attributes['aria-pressed'],'true');
    assert.equal(fixture({arrays:false,bundles:false,profile:false,zeroLights:true}).panels.length,0);
});

test('real GPU-only diagnostic controls switch measurement modes and reset the comparison window',async()=>{
    const f=fixture({arrays:false,bundles:false,gpu:true});
    assert.equal(f.panels.length,1);
    assert.equal(f.button('GPU por amostra').attributes['aria-pressed'],'true');
    await f.button('GPU desligada').events.click();
    await f.button('GPU contínua').events.click();
    await f.button('GPU por amostra').events.click();
    assert.deepEqual(f.calls,[['gpu','off'],['gpu','continuous'],['gpu','sampled']]);
    assert.equal(f.resets,3);
    assert.equal(f.button('GPU por amostra').attributes['aria-pressed'],'true');
    assert.ok(f.logs.every(line=>line.startsWith('[GpuTimingBenchmark]')));
    const unsupported=fixture({arrays:false,bundles:false,gpu:true,supported:false});
    assert.equal(unsupported.button('GPU contínua').disabled,true);
    assert.equal(unsupported.button('GPU por amostra').disabled,true);
    assert.equal(unsupported.button('GPU desligada').disabled,false);
    assert.equal(fixture({arrays:false,bundles:false,gpu:true,profile:false}).panels.length,0);
});

test('real device setup requests texture arrays by default and preserves an explicit zero escape',async()=>{
    const from=source.indexOf('async function createDevice('),to=source.indexOf('function enableTextureArrayBenchmark(',from);
    assert.ok(from>=0&&to>from);
    for(const [search,expected,profile]of [['',true,false],['?texture-arrays=0',false,false],['?profile=1',true,true],['?profile=1&texture-arrays=0',false,true]]) {
        const create=new Function('URLSearchParams','location','canvas','createMainGraphics',source.slice(from,to)+';return createDevice;')(
            URLSearchParams,{search},{},async(canvas,options)=>options);
        const options=await create();
        assert.equal(options.textureArrays,expected,search);assert.equal(options.profile,profile,search);
    }
});


test('internal post A/B waits for applied worker state without mutating quality or resolution', async () => {
    const f=fixture({arrays:false,bundles:false,worker:true,internalPost:true});
    assert.equal(f.button('Pós interno').attributes['aria-pressed'],'true');
    const pending=f.button('Pós original').events.click(); assert.equal(f.resets,0);
    await pending; await f.button('Pós interno').events.click();
    assert.deepEqual(f.calls,[['internalPost',false],['profile'],['internalPost',true],['profile']]);
    assert.equal(f.resets,2); assert.equal(f.button('Pós interno').attributes['aria-pressed'],'true');
    assert.ok(f.logs.every(line=>line.startsWith('[InternalPostBenchmark]')));
    assert.equal(fixture({arrays:false,bundles:false,profile:false,internalPost:true}).panels.length,0);
});
