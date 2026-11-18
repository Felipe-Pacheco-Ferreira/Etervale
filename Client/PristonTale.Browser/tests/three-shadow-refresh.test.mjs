import test from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from '../wwwroot/vendor/three/three.webgpu.js';
import { createShadowRefresh } from '../wwwroot/three-shadow-refresh.js';

function fixture() {
    const light = new THREE.DirectionalLight();light.castShadow = true;light.position.set(300, 1000, 200);
    light.updateMatrixWorld();light.target.updateMatrixWorld();
    const schedule = createShadowRefresh(),frame = { timeSeconds: 0, cameraTarget: [0,0,0], sunDirection: [0,-1,0] };
    let draws = 0;
    return { light,schedule,frame,get draws() { return draws; },draw(time,hz=30,enabled=true) {
        frame.timeSeconds=time;
        const due=schedule.update(light,frame,hz,enabled);
        if(due){light.updateMatrixWorld();light.target.updateMatrixWorld();light.shadow.updateMatrices(light);light.shadow.needsUpdate=false;draws++;}
        return due;
    }};
}
test('public LightShadow refresh requests retain30Hz deadlines at30,46and60Hz without per-frame rounding',()=>{
    for(const fps of [30,46,60]) {
        const f=fixture();for(let i=0;i<fps*4;i++)f.draw(i/fps);
        assert.equal(f.draws,120);assert.equal(f.light.shadow.autoUpdate,false);
        const stats=f.schedule.snapshot();assert.equal(stats.requested,120);assert.equal(stats.skipped,fps*4-120);
        assert.match(stats.measurement,/requests/);assert.equal(f.schedule.snapshot().requested,0);
    }
});
test('rate zero, rate changes, clock rollback and long stalls request current shadows without catch-up bursts',()=>{
    const f=fixture();for(let i=0;i<120;i++)assert.equal(f.draw(i/60,0),true);
    assert.equal(f.draw(2,30),true);assert.equal(f.draw(2+1/60,30),false);
    assert.equal(f.draw(2+1/60,60),true);assert.equal(f.draw(0,30),true);
    assert.equal(f.draw(10,30),true);assert.equal(f.draw(10,30),false);
    assert.equal(f.draw(10+1/60,30),false);assert.equal(f.draw(10+1/30,30),true);
    assert.equal(f.schedule.snapshot().timeResets,1);
});
test('skipped refreshes preserve actual Three shadow matrix, projection and map identity',()=>{
    const f=fixture(),map=new THREE.RenderTarget(16,16);f.light.shadow.map=map;f.draw(0);
    const matrix=f.light.shadow.matrix.clone(),view=f.light.shadow.camera.matrixWorldInverse.clone(),projection=f.light.shadow.camera.projectionMatrix.clone();
    f.light.position.x+=25;f.light.target.position.x+=25;
    assert.equal(f.draw(1/60),false);assert.equal(f.light.shadow.needsUpdate,false);
    assert.ok(f.light.shadow.matrix.equals(matrix));assert.ok(f.light.shadow.camera.matrixWorldInverse.equals(view));
    assert.ok(f.light.shadow.camera.projectionMatrix.equals(projection));assert.equal(f.light.shadow.map,map);
    assert.equal(f.draw(1/30),true);assert.equal(f.light.shadow.matrix.equals(matrix),false);assert.equal(f.light.shadow.map,map);map.dispose();
});
test('teleports, solar jumps, explicit invalidation and returning shadows refresh immediately',()=>{
    const f=fixture();f.draw(0);f.frame.cameraTarget[0]=64;assert.equal(f.draw(.001),false);
    f.frame.cameraTarget[0]=65;assert.equal(f.draw(.002),true);
    f.frame.sunDirection[0]=.02;assert.equal(f.draw(.003),true);
    f.schedule.invalidate();assert.equal(f.draw(.004),true);
    assert.equal(f.draw(.005,30,false),false);assert.equal(f.light.shadow.needsUpdate,false);
    assert.equal(f.draw(.006,30,true),true);f.light.shadow.needsUpdate=true;assert.equal(f.draw(.007),true);
    const stats=f.schedule.snapshot();assert.equal(stats.teleports,1);assert.equal(stats.directionChanges,1);
    assert.equal(stats.invalidations,1);assert.equal(stats.disabled,1);
});
test('invalid clocks and rates fail toward fresh shadows rather than freezing the last map',()=>{
    const f=fixture();f.draw(0);assert.equal(f.draw(Number.NaN),true);
    assert.equal(f.draw(.001,Number.NaN),true);assert.equal(f.draw(.002,-30),true);
    assert.throws(()=>f.schedule.update({},f.frame,30,true),TypeError);
});
