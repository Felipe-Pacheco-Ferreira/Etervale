import test from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from '../wwwroot/vendor/three/three.webgpu.js';
import { ExternalCamera } from '../wwwroot/three-camera.js';
import { createThreeGraphics } from '../wwwroot/three-scene.js';

function fakeRenderer() {
    return { coordinateSystem:THREE.WebGPUCoordinateSystem,
        backend:{isWebGPUBackend:true,device:{limits:{maxTextureArrayLayers:256}}},
        info:{render:{calls:0,triangles:0},reset(){}},
        setPixelRatio(){},setSize(){},setScissorTest(){},setRenderTarget(){},setViewport(){},
        render(){},dispose(){} };
}

async function fixture(unlit,cutout=false) {
    const graphics=await createThreeGraphics({dataset:{}},{renderer:fakeRenderer(),profile:true,textureArrays:true,cutoutArrays:cutout});
    graphics.createScene(1);
    const source={blend:'opaque',doubleSided:true,unlit,color:[.6,.8,.4,1],roughness:.37,metallic:.2,alphaCutoff:cutout ? .4 : 0};
    for(const id of [-1,-2]) {
        const rgba=new Uint8Array(4*4*4);
        for(let at=0;at<rgba.length;at+=4)rgba.set(id===-1?[220,50,80,255]:[40,180,110,255],at);
        if(cutout){rgba[3]=0;rgba[7]=127;}
        graphics.createTexture(id,4,4,rgba,false,1);
        graphics.configureSceneTexture(1,id);
        graphics.createMesh(1,-id,{positions:[1,0,0,2,0,0,1,1,0],normals:[0,0,1,0,0,1,0,0,1],
            uv:[-.25,0,2.75,0,-.25,3],colors:[.5,.8,.3,1,.8,.3,.6,1,.3,.6,.8,1],
            material:{...source,textureId:id}});
    }
    graphics.createStage(1,1,[1,2]);
    const stage=graphics.scene(1).stages.get(1);
    const batch=stage.userData.textureArrayBatches.batches[0];
    return {graphics,stage,batch,source};
}

test('production array materials build layered WGSL and GLSL while preserving authored PBR and vertex policies',async()=>{
    for(const unlit of [false,true]) {
        const {graphics,stage,batch,source}=await fixture(unlit);
        assert.ok(batch,'two distinct immutable albedos must form a real array batch');
        const material=batch.material;
        assert.equal(material.map,null);
        assert.equal(material.userData.arrayTexture.isDataArrayTexture,true);
        assert.equal(material.userData.arrayTexture.colorSpace,THREE.SRGBColorSpace);
        assert.deepEqual(material.color.toArray(),source.color.slice(0,3));
        assert.equal(material.roughness,source.roughness);
        assert.equal(material.metalness,source.metallic);
        assert.equal(material.side,THREE.DoubleSide);
        assert.equal(material.vertexColors,true);
        assert.equal(material.depthWrite,true);
        assert.ok(stage.children.every(object=>object.castShadow&&object.receiveShadow));
        assert.deepEqual(new Set(batch.geometry.getAttribute('textureLayer').array),new Set([0,1]));
        const mesh=stage.children.find(object=>object.geometry===batch.geometry);
        for(const forceWebGL of [false,true]) {
            const compiler=new THREE.WebGPURenderer({forceWebGL,canvas:{width:128,height:96,style:{},addEventListener(){},removeEventListener(){}}});
            compiler.hasFeature=()=>false;compiler._bindings=undefined;
            const builder=compiler.backend.createNodeBuilder(mesh,compiler);
            builder.camera=new ExternalCamera(forceWebGL?THREE.WebGLCoordinateSystem:THREE.WebGPUCoordinateSystem);
            builder.scene=graphics.scene(1).scene;builder.sortBindingGroups=()=>{};
            builder.build();
            assert.match(builder.vertexShader,/textureLayer/);
            assert.match(builder.vertexShader,/flat[^\n]*noxiusTextureLayer|noxiusTextureLayer[^\n]*flat/,
                'a constant primitive layer must not round to a neighbouring array image during interpolation');
            assert.match(builder.fragmentShader,forceWebGL?/sampler2DArray/:/texture_2d_array/);
            assert.match(builder.fragmentShader,forceWebGL?/texture\(/:/textureSample\(/);
            assert.match(builder.vertexShader,/\bcolor\b/,'source vertex colour is carried into the array shader');
            assert.match(builder.fragmentShader,/DiffuseColor\s*=.*\*\s*nodeVarying\d+/,'vertex colour modulation survives the custom layer lookup');
        }
        await graphics.dispose();
    }
});

test('cutout array colour and actual pinned shadow graphs compile alpha clipping for both backend languages',async()=>{
    for(const unlit of [false,true]) {
        const {graphics,stage,batch}=await fixture(unlit,true);
        const mesh=stage.children.find(object=>object.geometry===batch.geometry);
        assert.equal(mesh.material.alphaTest,.4);
        for(const forceWebGL of [false,true]) {
            const compiler=new THREE.WebGPURenderer({forceWebGL,canvas:{width:128,height:96,style:{},addEventListener(){},removeEventListener(){}}});
            compiler.hasFeature=()=>false;compiler._bindings=undefined;
            const shadow=new THREE.NodeMaterial();
            shadow.isShadowPassMaterial=true;
            shadow.colorNode=compiler._getShadowNodes(mesh.material).colorNode;
            shadow.alphaTest=mesh.material.alphaTest;
            const shadowMesh=new THREE.Mesh(batch.geometry,shadow);
            for(const object of [mesh,shadowMesh]) {
                const builder=compiler.backend.createNodeBuilder(object,compiler);
                builder.camera=new ExternalCamera(forceWebGL?THREE.WebGLCoordinateSystem:THREE.WebGPUCoordinateSystem);
                builder.scene=graphics.scene(1).scene;builder.sortBindingGroups=()=>{};
                builder.build();
                assert.match(builder.fragmentShader,forceWebGL?/sampler2DArray/:/texture_2d_array/);
                assert.match(builder.fragmentShader,/discard/,'colour and shadow passes must clip sampled layer alpha');
            }
            shadow.dispose();
        }
        await graphics.dispose();
    }
});
