import test from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from '../wwwroot/vendor/three/three.webgpu.js';
import { createThreeGraphics } from '../wwwroot/three-scene.js';

const { FULL, SHARED, NONE } = THREE.RenderObjectRefreshType;

async function fixture(options = {}, source = {}) {
    const renderer = { coordinateSystem: THREE.WebGPUCoordinateSystem, backend: { isWebGPUBackend: true },
        info: { render: {}, reset() {} }, setPixelRatio() {}, render() {}, dispose() {} };
    const graphics = await createThreeGraphics({ dataset: {} }, { renderer, ...options });
    const geometry = new THREE.BufferGeometry().setAttribute('position', new THREE.Float32BufferAttribute([-1,-1,0,1,-1,0,0,1,0],3));
    geometry.setIndex([0,1,2]); geometry.computeBoundingBox();
    geometry.setAttribute('uv', new THREE.Float32BufferAttribute([0,0,1,0,0,1],2));
    geometry.setAttribute('textureLayer', new THREE.Float32BufferAttribute([0,0,0],1));
    const page = new THREE.DataArrayTexture(new Uint8Array([255,0,0,255]),1,1,1);
    const material = graphics.arrayMaterial({ material: { unlit:true, blend:'opaque', opacity:1, ...source } },page,geometry);
    const object = new THREE.Mesh(geometry,material); object.updateMatrixWorld(true);
    const target = new THREE.RenderTarget(128,96);
    const engine = { getRenderTarget() { return null; }, getMRT() { return null; }, getOutputRenderTarget() { return null; }, getCanvasTarget() { return target; } };
    const renderObject = { object, material, geometry, scene:new THREE.Scene(), renderer:engine, context:{width:128,height:96},
        _geometries:{attributes:{disposeVersion:0,has() { return true; }}},lightsNode:{getBuiltinLights() { return []; }},
        getVertexBuffers() { return Object.values(geometry.attributes); }, getIndex() { return geometry.index; },bundle:{static:true,version:0} };
    const builder = { material, object, context:{} }, observer = material.setupObserver(builder);
    let renderId = 0;
    return { graphics, geometry, page, material, object, target, renderObject, builder, observer,
        next() { return observer.needsRefresh(renderObject,{renderId:++renderId,renderer:engine}); },
        same() { return observer.needsRefresh(renderObject,{renderId,renderer:engine}); },
        async dispose() { geometry.dispose();material.dispose();page.dispose();target.dispose();await graphics.dispose(); } };
}

test('static array observers remain absent unless both diagnostic options explicitly request them',async()=>{
    for(const options of [{},{profile:true},{staticArrayObservers:true}]) {
        const f=await fixture(options);
        assert.equal(f.graphics.staticArrayObservers,null);
        assert.equal(f.graphics.setStaticArrayObserversEnabled(true),false);
        assert.equal(f.observer.hasNode,true);
        assert.equal(f.next(),FULL);
        await f.dispose();
    }
});

test('the owned array factory uses the public observer hook and same-instance toggles retain the original fallback',async()=>{
    const f=await fixture({profile:true,staticArrayObservers:true});
    assert.equal(f.observer.hasNode,false);
    assert.equal(new THREE.NodeMaterialObserver(f.builder).hasNode,true);
    assert.equal(f.next(),FULL);f.next();assert.equal(f.next(),SHARED);assert.equal(f.same(),NONE);
    const material=f.material, page=f.page;
    for(let round=0;round<3;round++) {
        assert.equal(f.graphics.setStaticArrayObserversEnabled(false),false);
        assert.equal(f.next(),FULL);
        assert.equal(f.graphics.setStaticArrayObserversEnabled(true),true);
        assert.equal(f.next(),SHARED);
        assert.equal(f.material,material);
        assert.equal(f.material.userData.arrayTexture,page);
    }
    const counters=f.graphics.getProfile().staticArrayObservers;
    assert.equal(counters.enabled,true);
    assert.equal(counters.branded,1);
    assert.ok(counters.shared>0&&counters.fallback>0);
    assert.equal(f.graphics.getProfile().staticArrayObservers.shared,0);
    await f.dispose();
    assert.equal(f.graphics.staticArrayObservers.snapshot().branded,0);
});

test('static bundle observers preserve standard comparisons for model, material, geometry, output and hidden array dependencies',async()=>{
    const f=await fixture({profile:true,staticArrayObservers:true});f.next();f.next();f.next();
    for(const mutate of [
        ()=>f.material.color.setRGB(.4,.6,.8),
        ()=>{f.material.roughness=.2;},
        ()=>f.object.matrixWorld.makeTranslation(4,2,1),
        ()=>{f.geometry.attributes.position.needsUpdate=true;},
        ()=>f.geometry.setDrawRange(0,0),
        ()=>{f.page.needsUpdate=true;},
        ()=>f.page.offset.set(.25,.5),
        ()=>f.page.dispose(),
        ()=>{f.renderObject.context.width=256;}
    ]) { mutate();assert.equal(f.next(),FULL);f.next(); }
    const other=new THREE.RenderTarget(64,64);
    f.renderObject.renderer.getCanvasTarget=()=>other;
    assert.equal(f.next(),FULL);
    other.dispose();await f.dispose();
});

test('wind, actors and unexpected nodes keep the original observer instead of freezing dynamic uniforms',async()=>{
    const wind=await fixture({profile:true,staticArrayObservers:true},{windX:1});
    assert.equal(wind.observer.hasNode,true);assert.equal(wind.next(),FULL);await wind.dispose();
    const f=await fixture({profile:true,staticArrayObservers:true});f.next();f.next();
    f.material.positionNode=THREE.TSL.positionLocal;
    assert.equal(f.next(),FULL);
    f.material.positionNode=null;f.material.userData.actor=true;
    assert.equal(f.next(),FULL);
    f.material.userData.actor=false;f.material.colorNode=THREE.TSL.vec4(1);
    assert.equal(f.next(),FULL);
    assert.ok(f.graphics.staticArrayObservers.snapshot().fallback>=3);
    await f.dispose();
});
