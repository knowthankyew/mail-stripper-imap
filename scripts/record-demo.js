import path from 'path';
import fs from 'fs';
import os from 'os';
import { fileURLToPath, pathToFileURL } from 'url';
import { execSync } from 'child_process';
import { createRequire } from 'module';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const require = createRequire(import.meta.url);

function resolvePlaywright(repoRoot) {
  const candidates = [
    'playwright-core',
    'playwright',
    '@playwright/test',
    path.resolve(repoRoot, 'node_modules/playwright-core'),
    path.resolve(repoRoot, 'node_modules/playwright'),
    path.resolve(repoRoot, '../node_modules/playwright-core'),
    path.resolve(repoRoot, '../node_modules/playwright'),
    path.resolve(repoRoot, '../node_modules/@playwright/test'),
  ];
  for (const c of candidates) {
    try {
      const mod = require(c);
      if (mod.chromium) return mod.chromium;
    } catch {}
  }
  throw new Error('Playwright not found in repo or portfolio root. Run npm install at repo or portfolio root.');
}

function resolveFfmpeg(repoRoot) {
  if (process.env.FFMPEG_PATH && fs.existsSync(process.env.FFMPEG_PATH)) {
    return process.env.FFMPEG_PATH;
  }
  try {
    const sys = execSync('which ffmpeg', { encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] }).trim();
    if (sys && fs.existsSync(sys)) return sys;
  } catch {}

  const standardPaths = [
    path.resolve(repoRoot, 'node_modules/ffmpeg-static/ffmpeg'),
    path.resolve(repoRoot, '../node_modules/ffmpeg-static/ffmpeg'),
    '/opt/homebrew/bin/ffmpeg',
    '/usr/local/bin/ffmpeg',
  ];
  for (const p of standardPaths) {
    if (fs.existsSync(p)) return p;
  }

  // macOS-specific cache lookup for Playwright-managed ffmpeg builds;
  // non-macOS environments rely on system ffmpeg or ffmpeg-static above.
  const cacheDir = path.join(os.homedir(), 'Library/Caches/ms-playwright');
  if (fs.existsSync(cacheDir)) {
    for (const entry of fs.readdirSync(cacheDir)) {
      if (entry.startsWith('ffmpeg-')) {
        const bin = path.join(cacheDir, entry, 'ffmpeg-mac');
        if (fs.existsSync(bin)) return bin;
      }
    }
  }
  return 'ffmpeg';
}

async function sleep(ms) {
  return new Promise(resolve => setTimeout(resolve, ms));
}

(async () => {
  const repoRoot = path.resolve(__dirname, '..');
  const chromium = resolvePlaywright(repoRoot);
  const tempVideoDir = path.join(repoRoot, '.temp_demo_videos');
  if (!fs.existsSync(tempVideoDir)) fs.mkdirSync(tempVideoDir, { recursive: true });

  const visualizerHtml = path.join(repoRoot, 'scripts/demo-visualizer/index.html');
  if (!fs.existsSync(visualizerHtml)) {
    throw new Error(`Demo visualizer not found at ${visualizerHtml}`);
  }

  // Look for standard macOS browser installation; on Linux/Windows, Playwright launches
  // its default bundled Chromium binary automatically when executablePath is undefined.
  let browserExecutable = undefined;
  const browserCandidates = [
    '/Applications/Brave Browser.app/Contents/MacOS/Brave Browser',
    '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
  ];
  for (const b of browserCandidates) {
    if (fs.existsSync(b)) {
      browserExecutable = b;
      break;
    }
  }

  console.log(`🎬 Launching Playwright browser for mail-stripper-imap demo recording...`);
  const launchOptions = {
    headless: true,
    args: ['--no-sandbox', '--disable-gpu', '--window-size=1366,860']
  };
  if (browserExecutable) {
    launchOptions.executablePath = browserExecutable;
  }

  const browser = await chromium.launch(launchOptions);

  try {
    const context = await browser.newContext({
      viewport: { width: 1366, height: 860 },
      recordVideo: {
        dir: tempVideoDir,
        size: { width: 1366, height: 860 }
      }
    });

    const page = await context.newPage();

    const visualizerUrl = pathToFileURL(visualizerHtml).href;
    console.log(`Step 1: Navigating to Protocol Simulation Visualizer (${visualizerUrl})...`);
    await page.goto(visualizerUrl, { waitUntil: 'load' });

    console.log('Step 2: Recording real-time protocol simulation and threat segregation...');
    // The in-browser simulation runs for ~16 seconds
    await sleep(18500);

    console.log('Step 3: Finalizing video recording...');
    await page.close();
    await context.close();
  } finally {
    await browser.close();
  }

  // Locate recorded video sorted by most recent modification time
  const videoFiles = fs.readdirSync(tempVideoDir)
    .filter(f => f.endsWith('.webm'))
    .map(f => ({ name: f, fullPath: path.join(tempVideoDir, f), mtimeMs: fs.statSync(path.join(tempVideoDir, f)).mtimeMs }))
    .sort((a, b) => b.mtimeMs - a.mtimeMs);

  if (videoFiles.length > 0) {
    const latestVideo = videoFiles[0].fullPath;
    const destMp4 = path.join(repoRoot, 'demo.mp4');
    const destGif = path.join(repoRoot, 'demo.gif');
    const ffmpegPath = resolveFfmpeg(repoRoot);

    console.log(`Converting recording to web-optimized MP4 (H.264) via ${ffmpegPath}...`);
    try {
      execSync(`"${ffmpegPath}" -y -i "${latestVideo}" -c:v libx264 -pix_fmt yuv420p -movflags +faststart "${destMp4}"`, { stdio: 'inherit' });
      const statsMp4 = fs.statSync(destMp4);
      console.log(`\n🎉 Demo video recorded and saved to: ${destMp4} (${(statsMp4.size / (1024 * 1024)).toFixed(2)} MB)\n`);

      console.log(`Generating animated demo.gif for GitHub README...`);
      execSync(`"${ffmpegPath}" -y -i "${destMp4}" -vf "fps=10,scale=800:-1:flags=lanczos,split[s0][s1];[s0]palettegen=max_colors=128[p];[s1][p]paletteuse=dither=bayer" "${destGif}"`, { stdio: 'inherit' });
      const statsGif = fs.statSync(destGif);
      console.log(`🎉 Demo GIF generated and saved to: ${destGif} (${(statsGif.size / (1024 * 1024)).toFixed(2)} MB)\n`);
    } catch (e) {
      console.warn('FFmpeg conversion error:', e.message);
      fs.copyFileSync(latestVideo, destMp4.replace('.mp4', '.webm'));
    }

    fs.rmSync(tempVideoDir, { recursive: true, force: true });
  } else {
    console.error('❌ Error: No recorded video file was created in temporary video directory.');
    process.exit(1);
  }
})();
