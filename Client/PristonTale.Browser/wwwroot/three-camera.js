import { Camera, WebGPUCoordinateSystem } from './vendor/three/three.webgpu.js';

export class ExternalCamera extends Camera {
    constructor(coordinateSystem) {
        super();
        this.coordinateSystem = coordinateSystem;
        this.matrixAutoUpdate = false;
        this.matrixWorldAutoUpdate = false;
        this.updateProjectionMatrix();
    }

    // Projection is supplied by the C# camera; renderer setup must not rebuild it from an unrelated field of view.
    updateProjectionMatrix() {
        this.projectionMatrixInverse.copy(this.projectionMatrix).invert();
        const inverse = this.projectionMatrixInverse.elements;
        const clipNear = this.coordinateSystem === WebGPUCoordinateSystem ? 0 : -1;
        const near = Math.abs((inverse[10] * clipNear + inverse[14]) / (inverse[11] * clipNear + inverse[15]));
        const far = Math.abs((inverse[10] + inverse[14]) / (inverse[11] + inverse[15]));
        this.near = Math.max(0.0001, near);
        this.far = Number.isFinite(far) ? Math.max(this.near + 0.0001, far) : 1000000;
        this.isPerspectiveCamera = Math.abs(this.projectionMatrix.elements[11]) > 0.5;
        this.isOrthographicCamera = !this.isPerspectiveCamera;
        this.fov = this.isPerspectiveCamera ? 2 * Math.atan(1 / Math.abs(this.projectionMatrix.elements[5])) * 180 / Math.PI : 0;
        this.aspect = this.projectionMatrix.elements[5] / this.projectionMatrix.elements[0];
        this.zoom = 1;
    }
}
