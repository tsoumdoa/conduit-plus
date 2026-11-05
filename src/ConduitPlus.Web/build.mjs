import { build } from 'esbuild';
import { mkdir, writeFile } from 'node:fs/promises';
const result = await build({entryPoints:['runtime.ts'],bundle:true,minify:true,write:false,target:'es2022'});
await mkdir('dist',{recursive:true});
await writeFile('dist/cards.html',`<!doctype html><html><head><meta charset="utf-8"><meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src data:"><style>*{box-sizing:border-box}html,body{margin:0;background:transparent;overflow:hidden}.card{position:absolute;overflow:hidden}span{position:absolute;white-space:nowrap;overflow:hidden} .box{position:absolute}</style></head><body><script>${result.outputFiles[0].text}</script></body></html>`);
