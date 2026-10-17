import manifest from '../artifacts/manifest.json';
import { createWorker } from './download.mjs';

export default createWorker(manifest);
