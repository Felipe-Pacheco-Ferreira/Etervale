import { dotnet } from './_framework/dotnet.js';

const report = document.getElementById('report');
const canvas = document.getElementById('canvas');
// The checks are read again every so many frames, so resizing the window shows whether the back buffer follows.
const ReportEveryFrames = 30;
// The largest difference per channel a sample may have from its expected colour (8-bit targets round).
const Tolerance = 3;

// Reads the canvas in the task that drew it, before the browser presents it and its texture is discarded.
function sampleCanvas(samples) {
  const copy = new OffscreenCanvas(canvas.width, canvas.height);
  const context = copy.getContext('2d');
  context.drawImage(canvas, 0, 0);
  return samples.split('\n').map((line) => {
    const [x, y, r, g, b, ...name] = line.split(',');
    const pixel = context.getImageData(Number(x), Number(y), 1, 1).data;
    const expected = [Number(r), Number(g), Number(b)];
    const passed = expected.every((value, channel) => Math.abs(value - pixel[channel]) <= Tolerance);
    const found = `${pixel[0]},${pixel[1]},${pixel[2]}`;
    return `${passed ? 'ok  ' : 'FAIL'} ${name.join(',')} has its colour (${found}, expected ${expected.join(',')})`;
  });
}

try {
  if (!navigator.gpu) {
    throw new Error('this browser has no WebGPU');
  }

  // Device creation is asynchronous in the browser, so it happens here, before the runtime starts; Emscripten hands the
  // device to the native code (emscripten_webgpu_get_device).
  const adapter = await navigator.gpu.requestAdapter();
  if (!adapter) {
    throw new Error('no WebGPU adapter');
  }

  const device = await adapter.requestDevice();
  device.addEventListener('uncapturederror', (event) => console.error(`WebGPU: ${event.error.message}`));
  const runtime = await dotnet.withModuleConfig({ preinitializedWebGPUDevice: device }).create();
  const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
  const repro = exports.WebGpuRepro;
  repro.Start();

  let frames = 0;
  const frame = () => {
    repro.Frame();
    frames++;
    if (frames % ReportEveryFrames === 0) {
      report.textContent = [repro.Report(), ...sampleCanvas(repro.SpriteSamples())].join('\n');
    }

    requestAnimationFrame(frame);
  };
  requestAnimationFrame(frame);
} catch (error) {
  report.textContent = `FAIL ${error}`;
}
