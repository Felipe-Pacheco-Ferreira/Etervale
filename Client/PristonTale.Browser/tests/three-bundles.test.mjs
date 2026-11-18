import test from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from '../wwwroot/vendor/three/three.webgpu.js';
import { createStageBundleCache } from '../wwwroot/three-bundles.js';
import { createThreeGraphics } from '../wwwroot/three-scene.js';

function mesh(x = 0, z = 0, source = {}) {
    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute('position', new THREE.Float32BufferAttribute([x,0,z,x+2,0,z,x,3,z+2],3));
    geometry.setIndex([0,1,2]);
    geometry.computeBoundingBox();geometry.computeBoundingSphere();
    const material = new THREE.MeshBasicNodeMaterial({blending:THREE.NoBlending});
    material.userData.source = {opacity:1,blend:'opaque',...source};
    const result = new THREE.Mesh(geometry,material);
    result.matrixAutoUpdate=false;result.matrixWorldAutoUpdate=false;
    return result;
}
function fixture(objects, webgpu = true) {
    const prior = new THREE.StandardDrawingPipeline();
    const renderer = {drawingPipeline:prior,backend:{isWebGPUBackend:webgpu,beginBundle(){}},sortObjects:false};
    const cache = createStageBundleCache(THREE,renderer,true);
    const stage = new THREE.Group();stage.name='stage-test';stage.userData.renderObjects=objects;
    stage.add(...objects);cache.register(stage);
    return {cache,renderer,prior,stage};
}

test('static opaque and cutout wind meshes keep original resources inside conservative spatial command-cache chunks', () => {
    const objects=[mesh(-1200,0),mesh(-1190,0,{alphaCutoff:.25,windX:5,windZ:7}),mesh(1200,0)];
    objects[1].material.alphaTest=.25;
    let disposals=0;
    for(const object of objects) {
        object.geometry.addEventListener('dispose',()=>disposals++);
        object.material.addEventListener('dispose',()=>disposals++);
    }
    const f=fixture(objects);
    assert.equal(f.cache.snapshot().chunks,2);
    assert.equal(f.cache.snapshot().meshes,3);
    const point=new THREE.Vector3();
    for(const chunk of f.stage.children) {
        assert.equal(chunk.isBundleGroup,true);
        const bounds=chunk.userData.stageBundleChunk;
        for(const object of chunk.children) {
            const attribute=object.geometry.getAttribute('position');
            const source=object.material.userData.source;
            for(let at=0;at<attribute.count;at++) {
                point.fromBufferAttribute(attribute,at);
                for(const dx of[-Math.abs(source.windX??0),Math.abs(source.windX??0)])for(const dz of[-Math.abs(source.windZ??0),Math.abs(source.windZ??0)]) {
                    const offset=point.clone().add(new THREE.Vector3(dx,0,dz));
                    assert.ok(bounds.box.containsPoint(offset));
                    assert.ok(bounds.bounds.distanceToPoint(offset)<=1e-5);
                }
            }
        }
    }
    assert.deepEqual(new Set(f.stage.children.flatMap(chunk=>chunk.children)),new Set(objects));
    f.cache.dispose();
    assert.deepEqual(f.stage.children,objects);
    assert.equal(f.renderer.drawingPipeline,f.prior);
    assert.equal(disposals,0,'command-cache disposal never owns geometry or materials');
});

test('transparency, skinning, texture animation, dynamic geometry and matrix-driven meshes remain normal', () => {
    const skipped=[];
    const add=()=>{const value=mesh();skipped.push(value);return value;};
    add().material.transparent=true;
    add().material.blending=THREE.CustomBlending;
    add().isSkinnedMesh=true;
    add().material.userData.actor=true;
    add().material.userData.source.textureIds=[-1,-2];
    add().material.userData.source.blink=true;
    add().material.opacity=.5;
    add().material.depthWrite=false;
    add().geometry.getAttribute('position').setUsage(THREE.DynamicDrawUsage);
    add().matrixAutoUpdate=true;
    add().matrixWorldAutoUpdate=true;
    add().renderOrder=5;
    const staticMesh=mesh();
    const f=fixture([...skipped,staticMesh]);
    assert.equal(f.cache.snapshot().meshes,1);
    assert.ok(skipped.every(object=>object.parent===f.stage));
    assert.equal(staticMesh.parent.isBundleGroup,true);
    f.cache.dispose();
});

test('large crossing geometry owns an independent chunk instead of weakening nearby chunk culling', () => {
    const wide=mesh();
    wide.geometry.getAttribute('position').setXYZ(1,7000,0,0);wide.geometry.computeBoundingBox();
    const local=mesh();
    const f=fixture([wide,local]);
    assert.equal(f.stage.children.length,2);
    assert.notEqual(wide.parent,local.parent);
    assert.ok(wide.parent.name.includes('wide-'));
    f.cache.dispose();
});

test('per-camera pipeline culls a chunk independently for the main and shadow views', () => {
    const object=mesh();const f=fixture([object]);
    const chunk=object.parent;
    // Disable only backend recording for this headless projection; the production chunk predicate still runs.
    delete f.renderer.backend.beginBundle;
    const pipeline=f.renderer.drawingPipeline;pipeline.renderer=f.renderer;
    const main=new THREE.OrthographicCamera(-10,10,10,-10,.1,100);
    main.position.z=10;main.updateMatrixWorld();main.updateProjectionMatrix();
    const projected=[];const list={push(object){projected.push(object);}};
    pipeline.begin(main,{frustumArray:null});pipeline.project(f.stage,main,0,list,null);
    assert.deepEqual(projected,[object]);
    chunk.userData.stageBundleChunk.bounds.center.x=1000;
    projected.length=0;
    pipeline.begin(main,{frustumArray:null});pipeline.project(f.stage,main,0,list,null);
    assert.equal(projected.length,0,'chunk is excluded before child submission');
    const shadow=new THREE.OrthographicCamera(-2000,2000,2000,-2000,.1,100);
    shadow.position.z=10;shadow.updateMatrixWorld();shadow.updateProjectionMatrix();
    pipeline.begin(shadow,{frustumArray:null});pipeline.project(f.stage,shadow,0,list,null);
    assert.deepEqual(projected,[object],'a separate shadow view does not inherit main-view visibility');
    f.cache.dispose();
});

test('invalidation and same-instance toggles preserve flat ownership and release former static cache assumptions', () => {
    const objects=[mesh(),mesh(4)];const f=fixture(objects);
    const oldChunk=objects[0].parent;const initial=oldChunk.version;
    f.cache.invalidate(f.stage);assert.equal(oldChunk.version,initial+1);
    f.cache.prepare(f.stage,2,true);
    const prepared=oldChunk.version;
    f.cache.prepare(f.stage,2,true);assert.equal(oldChunk.version,prepared,'unchanged layouts do not re-record commands');
    f.cache.prepare(f.stage,3,true);assert.equal(oldChunk.version,prepared+1,'light-count layout changes invalidate capture');
    f.cache.prepare(f.stage,3,false);assert.equal(oldChunk.version,prepared+2,'shadow pipeline changes invalidate capture');
    f.cache.setEnabled(false);
    assert.equal(oldChunk.static,false);
    assert.deepEqual(f.stage.children,objects);
    assert.equal(f.cache.snapshot().meshes,0);
    f.cache.setEnabled(true);
    assert.notEqual(objects[0].parent,oldChunk);
    assert.equal(f.cache.snapshot().meshes,2);
    f.cache.unregister(f.stage);
    assert.deepEqual(f.stage.children,objects);
    assert.equal(f.cache.snapshot().stages,0);
    f.cache.dispose();
});

test('WebGL fallback, default-off and custom drawing pipelines retain normal group behaviour', () => {
    const object=mesh();const fallback=fixture([object],false);
    assert.equal(fallback.cache.supported,false);
    assert.equal(object.parent,fallback.stage);
    assert.equal(fallback.renderer.drawingPipeline,fallback.prior);
    fallback.cache.dispose();
    const prior=new THREE.StandardDrawingPipeline();
    const renderer={drawingPipeline:prior,backend:{isWebGPUBackend:true,beginBundle(){}}};
    const off=createStageBundleCache(THREE,renderer,false);
    assert.equal(off.enabled,false);assert.equal(renderer.drawingPipeline,prior);off.dispose();
    renderer.drawingPipeline={custom:true};
    const custom=createStageBundleCache(THREE,renderer,true);
    assert.equal(custom.supported,false);assert.deepEqual(renderer.drawingPipeline,{custom:true});custom.dispose();
    assert.throws(()=>createStageBundleCache(THREE,renderer,true,{chunkSize:0}),RangeError);
});

async function driverFixture(webgpu = true) {
    const renderer = {coordinateSystem:webgpu?THREE.WebGPUCoordinateSystem:THREE.WebGLCoordinateSystem,
        drawingPipeline:new THREE.StandardDrawingPipeline(),backend:{isWebGPUBackend:webgpu,beginBundle(){}},
        info:{render:{drawCalls:0,triangles:0,frameCalls:0},reset(){this.render.drawCalls=0;this.render.triangles=0;this.render.frameCalls=0;}},
        setPixelRatio(){},setSize(){},setViewport(){},setScissorTest(){},setRenderTarget(target){this.target=target;},
        getRenderTarget(){return this.target;},render(){},dispose(){}};
    const graphics=await createThreeGraphics({dataset:{}},{renderer,profile:true,renderBundles:true});
    graphics.createScene(1);
    const descriptor=id=>({positions:[id,0,0,id+1,0,0,id,1,0],indices:[0,1,2],material:{blend:'opaque',color:[1,id*.2,.3,1]}});
    graphics.createMesh(1,1,descriptor(1));graphics.createMesh(1,2,descriptor(2));graphics.createStage(1,1,[1,2]);
    return{graphics,renderer,descriptor};
}

test('production driver retains flat ownership for quality, profile census, invalidation and live toggles',async()=>{
    const{graphics}=await driverFixture();
    const stage=graphics.scene(1).stages.get(1),objects=stage.userData.renderObjects;
    assert.equal(objects.length,2);assert.equal(stage.children.length,1);assert.equal(stage.children[0].isBundleGroup,true);
    const source=graphics.scene(1).meshes.get(1);
    assert.equal(source.scenery,objects[0]);
    let disposed=0;source.geometry.addEventListener('dispose',()=>disposed++);
    graphics.configureScene(1,{quality:'High',shadowMapSize:0});
    const chunk=objects[0].parent,version=chunk.version;
    graphics.createTexture(-1,1,1,new Uint8Array([1,2,3,255]));
    assert.ok(chunk.version>version,'negative texture publication invalidates cached bindings');
    const profile=graphics.getProfile();
    assert.equal(profile.textureArrays.outputObjects,2,'census counts drawable meshes instead of bundle wrappers');
    assert.equal(profile.renderBundles.meshes,2);
    assert.equal(profile.renders.drawCalls,undefined,'replayed GPU draws are not mislabeled as zero actual draw calls');
    assert.ok(profile.renders.encodedDrawCalls);
    stage.position.set(7,3,-2);stage.visible=false;
    assert.equal(graphics.setRenderBundlesEnabled(false),false);
    assert.deepEqual(stage.children,objects);
    assert.equal(stage.visible,false);assert.deepEqual(stage.position.toArray(),[7,3,-2]);
    assert.equal(graphics.setRenderBundlesEnabled(true),true);
    assert.equal(source.scenery,objects[0]);assert.equal(disposed,0);
    graphics.removeMesh(1,1);
    assert.equal(disposed,1);
    assert.deepEqual(graphics.scene(1).stages.get(1).userData.meshIds,[2]);
    await graphics.dispose();assert.equal(disposed,1,'removed source geometry is never disposed a second time');
});

test('production render-bundle request on WebGL remains flat and reports ordinary draw counters',async()=>{
    const{graphics}=await driverFixture(false);
    const stage=graphics.scene(1).stages.get(1);
    assert.equal(stage.children.length,2);assert.ok(stage.children.every(object=>object.isMesh));
    assert.equal(graphics.setRenderBundlesEnabled(true),false);
    const profile=graphics.getProfile();
    assert.equal(profile.renderBundles.supported,false);assert.ok(profile.renders.drawCalls);
    await graphics.dispose();
});
