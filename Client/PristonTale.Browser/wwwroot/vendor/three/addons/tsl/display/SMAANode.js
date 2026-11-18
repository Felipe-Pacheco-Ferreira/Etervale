import { HalfFloatType, LinearFilter, NearestFilter, RenderTarget, Texture, Vector2, QuadMesh, NodeMaterial, Node, RendererUtils } from '../../../three.webgpu.js';
import { abs, Fn, NodeUpdateType, uv, uniform, convertToTexture, vec2, vec4, passTexture, max, step, dot, float, texture, If, Loop, int, Break, sqrt, sign, mix, context } from '../../../three.tsl.js';

const _quadMesh = /*@__PURE__*/ new QuadMesh();
const _size = /*@__PURE__*/ new Vector2();

let _rendererState;

/**
 * Post processing node for applying SMAA. Unlike FXAA, this node
 * should be applied before converting colors to sRGB. SMAA should produce
 * better results than FXAA but is also more expensive to execute.
 *
 * Used Preset: SMAA 1x Medium (with color edge detection)
 * Reference: {@link https://github.com/iryoku/smaa/releases/tag/v2.8}.
 *
 * @augments Node
 * @three_import import { smaa } from 'three/addons/tsl/display/SMAANode.js';
 */
class SMAANode extends Node {

	static get type() {

		return 'SMAANode';

	}

	/**
	 * Constructs a new SMAA node.
	 *
	 * @param {TextureNode} textureNode - The texture node that represents the input of the effect.
	 */
	constructor( textureNode ) {

		super( 'vec4' );

		/**
		 * The texture node that represents the input of the effect.
		 *
		 * @type {TextureNode}
		 */
		this.textureNode = textureNode;

		/**
		 * The `updateBeforeType` is set to `NodeUpdateType.FRAME` since the node renders
		 * its effect once per frame in `updateBefore()`.
		 *
		 * @type {string}
		 * @default 'frame'
		 */
		this.updateBeforeType = NodeUpdateType.FRAME;

		/**
		 * The render target used for the edges pass.
		 *
		 * @private
		 * @type {RenderTarget}
		 */
		this._renderTargetEdges = new RenderTarget( 1, 1, { depthBuffer: false, type: HalfFloatType } );
		this._renderTargetEdges.texture.name = 'SMAANode.edges';

		/**
		 * The render target used for the weights pass.
		 *
		 * @private
		 * @type {RenderTarget}
		 */
		this._renderTargetWeights = new RenderTarget( 1, 1, { depthBuffer: false, type: HalfFloatType } );
		this._renderTargetWeights.texture.name = 'SMAANode.weights';

		/**
		 * The render target used for the blend pass.
		 *
		 * @private
		 * @type {RenderTarget}
		 */
		this._renderTargetBlend = new RenderTarget( 1, 1, { depthBuffer: false, type: HalfFloatType } );
		this._renderTargetBlend.texture.name = 'SMAANode.blend';

		// textures

		const scope = this;

		const areaTextureImage = new Image();
		areaTextureImage.src = this._getAreaTexture();
		areaTextureImage.onload = function () {

			// assigning data to HTMLImageElement.src is asynchronous (see #15162)
			scope._areaTexture.needsUpdate = true;

		};

		/**
		 * Represents the "area" texture used by the SMAA implementation.
		 *
		 * @private
		 * @type {RenderTarget}
		 */
		this._areaTexture = new Texture();
		this._areaTexture.name = 'SMAANode.area';
		this._areaTexture.image = areaTextureImage;
		this._areaTexture.minFilter = LinearFilter;
		this._areaTexture.generateMipmaps = false;
		this._areaTexture.flipY = false;

		const searchTextureImage = new Image();
		searchTextureImage.src = this._getSearchTexture();
		searchTextureImage.onload = function () {

			// assigning data to HTMLImageElement.src is asynchronous (see #15162)
			scope._searchTexture.needsUpdate = true;

		};

		/**
		 * Represents the "search" texture used by the SMAA implementation.
		 *
		 * @private
		 * @type {RenderTarget}
		 */
		this._searchTexture = new Texture();
		this._searchTexture.name = 'SMAANode.search';
		this._searchTexture.image = searchTextureImage;
		this._searchTexture.magFilter = NearestFilter;
		this._searchTexture.minFilter = NearestFilter;
		this._searchTexture.generateMipmaps = false;
		this._searchTexture.flipY = false;

		/**
		 * A uniform node holding the inverse resolution value.
		 *
		 * @private
		 * @type {UniformNode<vec2>}
		 */
		this._invSize = uniform( new Vector2() );

		/**
		 * A uniform texture node holding the area texture.
		 *
		 * @private
		 * @type {TextureNode}
		 */
		this._areaTextureUniform = texture( this._areaTexture );

		/**
		 * A uniform texture node holding the search texture.
		 *
		 * @private
		 * @type {TextureNode}
		 */
		this._searchTextureUniform = texture( this._searchTexture );

		/**
		 * A uniform texture node representing the edges pass.
		 *
		 * @private
		 * @type {TextureNode}
		 */
		this._edgesTextureUniform = texture( this._renderTargetEdges.texture );

		/**
		 * A uniform texture node representing the weights pass.
		 *
		 * @private
		 * @type {TextureNode}
		 */
		this._weightsTextureUniform = texture( this._renderTargetWeights.texture );

		/**
		 * The node material that holds the TSL for rendering the edges pass.
		 *
		 * @private
		 * @type {NodeMaterial}
		 */
		this._materialEdges = new NodeMaterial();
		this._materialEdges.name = 'SMAANode.edges';

		/**
		 * The node material that holds the TSL for rendering the weights pass.
		 *
		 * @private
		 * @type {NodeMaterial}
		 */
		this._materialWeights = new NodeMaterial();
		this._materialWeights.name = 'SMAANode.weights';

		/**
		 * The node material that holds the TSL for rendering the blend pass.
		 *
		 * @private
		 * @type {NodeMaterial}
		 */
		this._materialBlend = new NodeMaterial();
		this._materialBlend.name = 'SMAANode.blend';

		/**
		 * The result of the effect is represented as a separate texture node.
		 *
		 * @private
		 * @type {PassTextureNode}
		 */
		this._textureNode = passTexture( this, this._renderTargetBlend.texture );

	}

	/**
	 * Returns the result of the effect as a texture node.
	 *
	 * @return {PassTextureNode} A texture node that represents the result of the effect.
	 */
	getTextureNode() {

		return this._textureNode;

	}

	/**
	 * Sets the size of the effect.
	 *
	 * @param {number} width - The width of the effect.
	 * @param {number} height - The height of the effect.
	 */
	setSize( width, height ) {

		this._invSize.value.set( 1 / width, 1 / height );

		this._renderTargetEdges.setSize( width, height );
		this._renderTargetWeights.setSize( width, height );
		this._renderTargetBlend.setSize( width, height );

	}

	/**
	 * This method is used to render the effect once per frame.
	 *
	 * @param {NodeFrame} frame - The current node frame.
	 */
	updateBefore( frame ) {

		const { renderer } = frame;

		_rendererState = RendererUtils.resetRendererState( renderer, _rendererState );

		//

		const size = renderer.getDrawingBufferSize( _size );
		this.setSize( size.width, size.height );

		// edges

		renderer.setRenderTarget( this._renderTargetEdges );

		_quadMesh.material = this._materialEdges;
		_quadMesh.render( renderer );

		// weights

		renderer.setRenderTarget( this._renderTargetWeights );

		_quadMesh.material = this._materialWeights;
		_quadMesh.render( renderer );

		// blend

		renderer.setRenderTarget( this._renderTargetBlend );

		_quadMesh.material = this._materialBlend;
		_quadMesh.render( renderer );

		// restore

		RendererUtils.restoreRendererState( renderer, _rendererState );

	}

	/**
	 * This method is used to setup the effect's TSL code.
	 *
	 * @param {NodeBuilder} builder - The current node builder.
	 * @return {PassTextureNode}
	 */
	setup( builder ) {

		const SMAA_THRESHOLD = 0.1;
		const SMAA_MAX_SEARCH_STEPS = 8;
		const SMAA_AREATEX_MAX_DISTANCE = 16;
		const SMAA_AREATEX_PIXEL_SIZE = vec2( 1 / 160, 1 / 560 );
		const SMAA_AREATEX_SUBTEX_SIZE = ( 1 / 7 );

		const textureNode = this.textureNode;
		const uvNode = textureNode.uvNode || uv();

		// edges

		const SMAAEdgeDetection = Fn( () => {

			const vOffset0 = vec4( uvNode.xy, uvNode.xy ).add( vec4( this._invSize.xy, this._invSize.xy ).mul( vec4( - 1.0, 0.0, 0.0, - 1.0 ) ) ).toVertexStage();
			const vOffset1 = vec4( uvNode.xy, uvNode.xy ).add( vec4( this._invSize.xy, this._invSize.xy ).mul( vec4( 1.0, 0.0, 0.0, 1.0 ) ) ).toVertexStage();
			const vOffset2 = vec4( uvNode.xy, uvNode.xy ).add( vec4( this._invSize.xy, this._invSize.xy ).mul( vec4( - 2.0, 0.0, 0.0, - 2.0 ) ) ).toVertexStage();

			const threshold = vec2( SMAA_THRESHOLD, SMAA_THRESHOLD );

			// Calculate color deltas:
			const delta = vec4().toVar();
			const C = this.textureNode.sample( uvNode ).rgb.toVar();

			// Calculate left and top deltas:
			const Cleft = this.textureNode.sample( vOffset0.xy ).rgb.toVar();
			let t = abs( C.sub( Cleft ) );
			delta.x = max( t.r, t.g, t.b );

			const Ctop = this.textureNode.sample( vOffset0.zw ).rgb.toVar();
			t = abs( C.sub( Ctop ) );
			delta.y = max( t.r, t.g, t.b );

			// We do the usual threshold:
			const edges = step( threshold, delta.xy ).toVar();

			// Then discard if there is no edge:
			dot( edges, vec2( 1.0, 1.0 ) ).equal( 0 ).discard();

			// Calculate right and bottom deltas:
			const Cright = this.textureNode.sample( vOffset1.xy ).rgb.toVar();
			t = abs( C.sub( Cright ) );
			delta.z = max( t.r, t.g, t.b );

			const Cbottom = this.textureNode.sample( vOffset1.zw ).rgb.toVar();
			t = abs( C.sub( Cbottom ) );
			delta.w = max( t.r, t.g, t.b );

			// Calculate the maximum delta in the direct neighborhood:
			let maxDelta = max( delta.x, delta.y, delta.z, delta.w ).toVar();

			// Calculate left-left and top-top deltas:
			const Cleftleft = this.textureNode.sample( vOffset2.xy ).rgb.toVar();
			t = abs( C.sub( Cleftleft ) );
			delta.z = max( t.r, t.g, t.b );

			const Ctoptop = this.textureNode.sample( vOffset2.zw ).rgb.toVar();
			t = abs( C.sub( Ctoptop ) );
			delta.w = max( t.r, t.g, t.b );

			// Calculate the final maximum delta:
			maxDelta = max( maxDelta, delta.z, delta.w );

			// Local contrast adaptation in action:
			edges.xy.mulAssign( vec2( step( float( 0.5 ).mul( maxDelta ), delta.xy ) ) );

			return vec4( edges, 0, 0 );

		} );

		// weights

		const SMAASearchLength = Fn( ( [ searchTex, e, bias, scale ] ) => {

			// Not required if searchTex accesses are set to point:
			// float2 SEARCH_TEX_PIXEL_SIZE = 1.0 / float2(66.0, 33.0);
			// e = float2(bias, 0.0) + 0.5 * SEARCH_TEX_PIXEL_SIZE + e * float2(scale, 1.0) * float2(64.0, 32.0) * SEARCH_TEX_PIXEL_SIZE;
			const coord = vec2( e ).toVar();
			coord.r = bias.add( coord.r.mul( scale ) );
			return float( 255 ).mul( searchTex.sample( coord ) ).r;

		} );

		const SMAAArea = Fn( ( [ areaTex, dist, e1, e2, offset ] ) => {

			// Rounding prevents precision errors of bilinear filtering:
			let texcoord = float( SMAA_AREATEX_MAX_DISTANCE ).mul( float( 4 ).mul( vec2( e1, e2 ) ).round() ).add( dist );

			// We do a scale and bias for mapping to texel space:
			texcoord = SMAA_AREATEX_PIXEL_SIZE.mul( texcoord ).add( float( 0.5 ).mul( SMAA_AREATEX_PIXEL_SIZE ) );

			// Move to proper place, according to the subpixel offset:
			texcoord.y.addAssign( float( SMAA_AREATEX_SUBTEX_SIZE ).mul( offset ) );

			return areaTex.sample( texcoord ).rg;

		} );

		const SMAASearchXLeft = Fn( ( [ edgesTex, searchTex, texcoord, end ] ) => {

			/**
			* @PSEUDO_GATHER4
			* This texcoord has been offset by (-0.25, -0.125) in the vertex shader to
			* sample between edge, thus fetching four edges in a row.
			* Sampling with different offsets in each direction allows to disambiguate
			* which edges are active from the four fetched ones.
			*/

			const e = vec2( 0.0, 1.0 ).toVar();
			const coord = vec2( texcoord ).toVar();

			Loop( { start: int( 0 ), end: int( SMAA_MAX_SEARCH_STEPS ), type: 'int', condition: '<' }, () => { // port note: Changed while to for

				e.assign( edgesTex.sample( coord ).rg );
				coord.subAssign( vec2( 2, 0 ).mul( this._invSize ) );

				If( coord.x.lessThanEqual( end ).or( e.g.lessThanEqual( float( 0.8281 ) ).or( e.r.notEqual( float( 0 ) ) ) ), () => {

					Break();

				} );

			} );

			// We correct the previous (-0.25, -0.125) offset we applied:
			coord.x.addAssign( float( 0.25 ).mul( this._invSize.x ) );

			// The searches are bias by 1, so adjust the coords accordingly:
			coord.x.addAssign( this._invSize.x );

			// Disambiguate the length added by the last step:
			coord.x.addAssign( float( 2 ).mul( this._invSize.x ) );
			coord.x.subAssign( this._invSize.x.mul( SMAASearchLength( searchTex, e, 0, 0.5 ) ) );

			return coord.x;

		} );

		const SMAASearchXRight = Fn( ( [ edgesTex, searchTex, texcoord, end ] ) => {

			const e = vec2( 0.0, 1.0 ).toVar();
			const coord = vec2( texcoord ).toVar();

			Loop( { start: int( 0 ), end: int( SMAA_MAX_SEARCH_STEPS ), type: 'int', condition: '<' }, () => { // port note: Changed while to for

				e.assign( edgesTex.sample( coord ).rg );
				coord.addAssign( vec2( 2, 0 ).mul( this._invSize ) );

				If( coord.x.greaterThanEqual( end ).or( e.g.lessThanEqual( float( 0.8281 ) ).or( e.r.notEqual( float( 0 ) ) ) ), () => {

					Break();

				} );

			} );

			coord.x.subAssign( float( 0.25 ).mul( this._invSize.x ) );
			coord.x.subAssign( this._invSize.x );
			coord.x.subAssign( float( 2 ).mul( this._invSize.x ) );
			coord.x.addAssign( this._invSize.x.mul( SMAASearchLength( searchTex, e, 0.5, 0.5 ) ) );

			return coord.x;

		} );

		const SMAASearchYUp = Fn( ( [ edgesTex, searchTex, texcoord, end ] ) => {

			const e = vec2( 1.0, 0.0 ).toVar();
			const coord = vec2( texcoord ).toVar();

			Loop( { start: int( 0 ), end: int( SMAA_MAX_SEARCH_STEPS ), type: 'int', condition: '<' }, () => { // port note: Changed while to for

				e.assign( edgesTex.sample( coord ).rg );
				coord.addAssign( vec2( 0, - 2 ).mul( this._invSize ) );

				If( coord.y.lessThanEqual( end ).or( e.r.lessThanEqual( float( 0.8281 ) ).or( e.g.notEqual( float( 0 ) ) ) ), () => {

					Break();

				} );

			} );

			coord.y.addAssign( float( 0.25 ).mul( this._invSize.y ) );
			coord.y.addAssign( this._invSize.y );
			coord.y.addAssign( float( 2 ).mul( this._invSize.y ) );
			coord.y.subAssign( this._invSize.y.mul( SMAASearchLength( searchTex, e.gr, 0, 0.5 ) ) );

			return coord.y;

		} );

		const SMAASearchYDown = Fn( ( [ edgesTex, searchTex, texcoord, end ] ) => {

			const e = vec2( 1.0, 0.0 ).toVar();
			const coord = vec2( texcoord ).toVar();

			Loop( { start: int( 0 ), end: int( SMAA_MAX_SEARCH_STEPS ), type: 'int', condition: '<' }, () => { // port note: Changed while to for

				e.assign( edgesTex.sample( coord ).rg );
				coord.subAssign( vec2( 0, - 2 ).mul( this._invSize ) );

				If( coord.y.greaterThanEqual( end ).or( e.r.lessThanEqual( float( 0.8281 ) ).or( e.g.notEqual( float( 0 ) ) ) ), () => {

					Break();

				} );

			} );

			coord.y.subAssign( float( 0.25 ).mul( this._invSize.y ) );
			coord.y.subAssign( this._invSize.y );
			coord.y.subAssign( float( 2 ).mul( this._invSize.y ) );
			coord.y.addAssign( this._invSize.y.mul( SMAASearchLength( searchTex, e.gr, 0.5, 0.5 ) ) );

			return coord.y;

		} );

		const SMAAWeights = Fn( () => {

			const vPixcoord = uvNode.xy.div( this._invSize ).toVertexStage();

			// We will use these offsets for the searches later on (see @PSEUDO_GATHER4):
			const vOffset0 = vec4( uvNode.xy, uvNode.xy ).add( vec4( this._invSize.xy, this._invSize.xy ).mul( vec4( - 0.25, - 0.125, 1.25, - 0.125 ) ) ).toVertexStage();
			const vOffset1 = vec4( uvNode.xy, uvNode.xy ).add( vec4( this._invSize.xy, this._invSize.xy ).mul( vec4( - 0.125, - 0.25, - 0.125, 1.25 ) ) ).toVertexStage();

			// And these for the searches, they indicate the ends of the loops:
			const vOffset2 = vec4( vOffset0.xz, vOffset1.yw ).add( vec4( - 2.0, 2.0, - 2.0, 2.0 ).mul( vec4( this._invSize.xx, this._invSize.yy ) ).mul( float( SMAA_MAX_SEARCH_STEPS ) ) ).toVertexStage();

			const weights = vec4( 0.0, 0.0, 0.0, 0.0 ).toVar();
			const subsampleIndices = vec4( 0.0, 0.0, 0.0, 0.0 ).toVar();

			const e = this._edgesTextureUniform.sample( uvNode ).rg.toVar();

			If( e.g.greaterThan( float( 0 ) ), () => { // Edge at north

				let d = vec2().toVar();

				// Find the distance to the left:

				const coordsLeft = vec2().toVar();
				coordsLeft.x = SMAASearchXLeft( this._edgesTextureUniform, this._searchTextureUniform, vOffset0.xy, vOffset2.x );
				coordsLeft.y = vOffset1.y; // offset[1].y = texcoord.y - 0.25 * resolution.y (@CROSSING_OFFSET)
				d.x = coordsLeft.x;

				// Now fetch the left crossing edges, two at a time using bilinear
				// filtering. Sampling at -0.25 (see @CROSSING_OFFSET) enables to
				// discern what value each edge has:
				const e1 = this._edgesTextureUniform.sample( coordsLeft ).r.toVar();

				// Find the distance to the right:
				const coordsRight = vec2().toVar();
				coordsRight.x = SMAASearchXRight( this._edgesTextureUniform, this._searchTextureUniform, vOffset0.zw, vOffset2.y );
				coordsRight.y = vOffset1.y;
				d.y = coordsRight.x;

				// We want the distances to be in pixel units (doing this here allow to
				// better interleave arithmetic and memory accesses):
				d = d.div( this._invSize.x ).sub( vPixcoord.x );

				// SMAAArea below needs a sqrt, as the areas texture is compressed quadratically:
				const sqrt_d = sqrt( abs( d ) );

				// Fetch the right crossing edges:
				const e2 = this._edgesTextureUniform.sample( coordsRight.add( vec2( 1, 0 ).mul( this._invSize ) ) ).r.toVar();
				weights.r = e2;

				// Get the area for this direction:
				weights.rg = SMAAArea( this._areaTextureUniform, sqrt_d, e1, e2, float( subsampleIndices.y ) );

			} );

			If( e.r.greaterThan( float( 0 ) ), () => { // Edge at west

				let d = vec2().toVar();

				// Find the distance to the top:

				const coordsUp = vec2().toVar();
				coordsUp.y = SMAASearchYUp( this._edgesTextureUniform, this._searchTextureUniform, vOffset1.xy, vOffset2.z );
				coordsUp.x = vOffset0.x; // offset[1].x = texcoord.x - 0.25 * resolution.x;
				d.x = coordsUp.y;

				// Fetch the top crossing edges:
				const e1 = this._edgesTextureUniform.sample( coordsUp ).g.toVar();

				// Find the distance to the bottom:
				const coordsDown = vec2().toVar();
				coordsDown.y = SMAASearchYDown( this._edgesTextureUniform, this._searchTextureUniform, vOffset1.zw, vOffset2.w );
				coordsDown.x = vOffset0.x;
				d.y = coordsDown.y;

				// We want the distances to be in pixel units:
				d = d.div( this._invSize.y ).sub( vPixcoord.y );

				// SMAAArea below needs a sqrt, as the areas texture is compressed quadratically:
				const sqrt_d = sqrt( abs( d ) );

				// Fetch the bottom crossing edges:
				const e2 = this._edgesTextureUniform.sample( coordsDown.add( vec2( 0, 1 ).mul( this._invSize ) ) ).g.toVar();

				// Get the area for this direction:
				weights.ba = SMAAArea( this._areaTextureUniform, sqrt_d, e1, e2, float( subsampleIndices.x ) );

			} );

			return weights;

		} );

		// blend

		const SMAABlend = Fn( () => {

			const vOffset1 = vec4( uvNode.xy, uvNode.xy ).add( vec4( this._invSize.xy, this._invSize.xy ).mul( vec4( 1.0, 0.0, 0.0, 1.0 ) ) ).toVertexStage();
			const result = vec4().toVar();

			// Fetch the blending weights for current pixel:

			const a = vec4().toVar();
			a.xz = this._weightsTextureUniform.sample( uvNode ).xz;
			a.y = this._weightsTextureUniform.sample( vOffset1.zw ).g;
			a.w = this._weightsTextureUniform.sample( vOffset1.xy ).a;

			// Is there any blending weight with a value greater than 0.0?

			If( dot( a, vec4( 1.0 ) ).lessThan( 1e-5 ), () => { // Edge at north

				result.assign( this.textureNode.sample( uvNode ) );

			} ).Else( () => {

				// Up to 4 lines can be crossing a pixel (one through each edge). We
				// favor blending by choosing the line with the maximum weight for each
				// direction:

				const offset = vec2().toVar();

				offset.x = a.a.greaterThan( a.b ).select( a.a, a.b.negate() ); // left vs. right
				offset.y = a.g.greaterThan( a.r ).select( a.g, a.r.negate() ); // top vs. bottom

				// Then we go in the direction that has the maximum weight:

				If( abs( offset.x ).greaterThan( abs( offset.y ) ), () => { // horizontal vs. vertical

					offset.y.assign( 0 );

				} ).Else( () => {

					offset.x.assign( 0 );

				} );

				// Fetch the opposite color and lerp by hand:

				const C = this.textureNode.sample( uvNode ).toVar();
				const texcoord = vec2( uvNode ).toVar();
				texcoord.addAssign( sign( offset ).mul( this._invSize ) );
				const Cop = this.textureNode.sample( texcoord ).toVar();
				const s = abs( offset.x ).greaterThan( abs( offset.y ) ).select( abs( offset.x ), abs( offset.y ) ).toVar();

				const mixed = mix( C, Cop, s );
				result.assign( mixed );

			 } );

			 return result;

		} );

		const sharedContext = context( builder.getSharedContext() );

		this._materialEdges.contextNode = sharedContext;
		this._materialEdges.fragmentNode = SMAAEdgeDetection();
		this._materialEdges.needsUpdate = true;

		this._materialWeights.contextNode = sharedContext;
		this._materialWeights.fragmentNode = SMAAWeights();
		this._materialWeights.needsUpdate = true;

		this._materialBlend.contextNode = sharedContext;
		this._materialBlend.fragmentNode = SMAABlend();
		this._materialBlend.needsUpdate = true;

		return this._textureNode;

	}

	/**
	 * Frees internal resources. This method should be called
	 * when the effect is no longer required.
	 */
	dispose() {

		super.dispose();

		this._renderTargetEdges.dispose();
		this._renderTargetWeights.dispose();
		this._renderTargetBlend.dispose();

		this._areaTexture.dispose();
		this._searchTexture.dispose();

		this._materialEdges.dispose();
		this._materialWeights.dispose();
		this._materialBlend.dispose();

	}

	/**
	 * Returns the area texture as a Base64 string.
	 *
	 * @private
	 * @return {string} The area texture.
	 */
	_getAreaTexture() {

		return new URL('./smaa-area.png', import.meta.url).href;

	}

	/**
	 * Returns the search texture as a Base64 string..
	 *
	 * @private
	 * @return {string} The search texture.
	 */
	_getSearchTexture() {

		return new URL('./smaa-search.png', import.meta.url).href;

	}

}

export default SMAANode;

/**
 * TSL function for creating a SMAA node for anti-aliasing via post processing.
 *
 * @tsl
 * @function
 * @param {Node<vec4>} node - The node that represents the input of the effect.
 * @returns {SMAANode}
 */
export const smaa = ( node ) => new SMAANode( convertToTexture( node ) );
