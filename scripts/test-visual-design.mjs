import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {createRequire} from 'node:module';
import vm from 'node:vm';
const require=createRequire(import.meta.url);const ts=require('typescript');const React=require('react');const {renderToStaticMarkup}=require('react-dom/server');
const code=ts.transpileModule(readFileSync(new URL('../apps/web/app/page.tsx',import.meta.url),'utf8'),{compilerOptions:{module:ts.ModuleKind.CommonJS,jsx:ts.JsxEmit.ReactJSX,target:ts.ScriptTarget.ES2020}}).outputText;
async function home(connected){const module={exports:{}};vm.runInNewContext(code,{exports:module.exports,process:{env:{}},URL,AbortSignal,fetch:async()=>({ok:connected,json:async()=>({name:'Verified Service Marketplace'})}),require:name=>name==='next/link'?{default:({children,...props})=>React.createElement('a',props,children)}:require(name)});return renderToStaticMarkup(await module.exports.default());}
for(const connected of [true,false]){const html=await home(connected);assert.match(html,/href="\/register"/);assert.match(html,/Make yourself at home/);assert.match(html,/href="\/services\?q=clean"/);assert.match(html,/aria-label="Main navigation"/);assert.match(html,/id="how-it-works"/);assert.match(html,/simulated verification checks/);assert.match(html,/No payments/);assert.match(html,connected?/Demo API connected/:/Demo API unavailable/);assert.doesNotMatch(html,/Booking is not available yet/);}
const css=readFileSync(new URL('../apps/web/app/visual-design.css',import.meta.url),'utf8');const postcss=require('postcss');assert.ok(postcss.parse(css).nodes.length);assert.match(css,/prefers-reduced-motion/);assert.match(css,/:focus-visible/);assert.match(css,/max-width:420px/);
console.log('PASS: landing-page calls to action, demo disclosures, API availability states, CSS syntax and accessibility/responsive rules. Visual browser QA remains separate.');
