export function zeroPointLightMode(value) {
    if (value === undefined) return false;
    if (value === true || value === false || value === 'auto') return value;
    throw new TypeError('Point-light mode must be false, true or auto.');
}

export function createZeroPointLighting(THREE, TSL) {
    const enabled = TSL.uniform(true).setGroup(TSL.renderGroup);
    const owned = new WeakSet();
    const counts = { branded: 0, nodes: 0, guardedPoints: 0, fallbackLights: 0 };
    const standardModel = THREE.MeshStandardNodeMaterial.prototype.setupLightingModel;
    class ZeroPointLights extends THREE.LightsNode {
        static get type() { return 'NoxiusZeroPointLights'; }

        customCacheKey() { return (super.customCacheKey() ^ 0x5a21b784) >>> 0; }

        setupLights(builder, lightNodes) {
            const reflected = builder.context.reflectedLight;
            reflected.directDiffuse.toStack();
            reflected.directSpecular.toStack();
            super.setupLights(builder, lightNodes);
        }

        setupDirectLight(builder, lightNode, data) {
            const material = builder.material;
            if (!owned.has(material) || material.constructor !== THREE.MeshStandardNodeMaterial
                || material.setupLightingModel !== standardModel || !(lightNode instanceof THREE.PointLightNode) || lightNode.light.castShadow) {
                counts.fallbackLights++;
                return super.setupDirectLight(builder, lightNode, data);
            }
            counts.guardedPoints++;
            const radiance = data.lightColor.toVar('noxiusPointRadiance_' + lightNode.id);
            // A disabled experiment executes the ordinary PBR path without replacing shaders or light resources.
            TSL.If(enabled.not().or(radiance.notEqual(TSL.vec3(0)).any()), () => {
                super.setupDirectLight(builder, lightNode, { ...data, lightColor: radiance });
            });
        }
    }

    class ZeroPointLighting extends THREE.Lighting {
        createNode(lights = []) {
            counts.nodes++;
            return new ZeroPointLights().setLights(lights);
        }

        brand(material) {
            if (material.constructor !== THREE.MeshStandardNodeMaterial || material.setupLightingModel !== standardModel) return false;
            if (!owned.has(material)) { owned.add(material); counts.branded++; }
            return true;
        }

        setEnabled(value) {
            if (typeof value !== 'boolean') throw new TypeError('Point-light optimization requires a boolean.');
            enabled.value = value;
            return value;
        }

        snapshot() { return { requested: true, enabled: enabled.value, ...counts }; }
    }

    return new ZeroPointLighting();
}
