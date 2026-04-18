(function(){const t=document.createElement("link").relList;if(t&&t.supports&&t.supports("modulepreload"))return;for(const n of document.querySelectorAll('link[rel="modulepreload"]'))i(n);new MutationObserver(n=>{for(const a of n)if(a.type==="childList")for(const c of a.addedNodes)c.tagName==="LINK"&&c.rel==="modulepreload"&&i(c)}).observe(document,{childList:!0,subtree:!0});function r(n){const a={};return n.integrity&&(a.integrity=n.integrity),n.referrerPolicy&&(a.referrerPolicy=n.referrerPolicy),n.crossOrigin==="use-credentials"?a.credentials="include":n.crossOrigin==="anonymous"?a.credentials="omit":a.credentials="same-origin",a}function i(n){if(n.ep)return;n.ep=!0;const a=r(n);fetch(n.href,a)}})();async function b(e,t){const r=await fetch(e,t),i=await r.json();return{ok:r.ok,body:i}}function f(e,t){return b(e,{method:"POST",headers:{"content-type":"application/json"},body:JSON.stringify(t)})}const u={health:()=>b("/api/health"),preprocess:e=>f("/api/preprocess",e),parse:e=>f("/api/parse",e),ast:e=>f("/api/ast",e),validate:e=>f("/api/validate",e),run:e=>f("/api/run",e),createSession:e=>f("/api/sessions",e),listSessions:()=>b("/api/sessions")};function C(e,t){e.innerHTML="";const r=document.createElement("div");r.className="app-shell";const i=document.createElement("header");i.className="app-header",r.appendChild(i);const n=document.createElement("h1");n.textContent="CCGNF Playground",i.appendChild(n);const a=document.createElement("nav");for(const p of t){const h=document.createElement("a");h.href=p.href,h.textContent=p.label,h.dataset.route=p.href.replace(/^#/,""),a.appendChild(h)}i.appendChild(a);const c=document.createElement("span");c.className="muted",c.innerHTML="port <span data-port>&mdash;</span>",i.appendChild(c);const d=document.createElement("span");d.className="muted health-pill",d.textContent="health: unknown",i.appendChild(d);const l=document.createElement("div");return l.className="route-view",l.id="route-view",r.appendChild(l),e.appendChild(r),v(a),window.addEventListener("hashchange",()=>v(a)),E(c,d),l}function v(e){const t=(window.location.hash||"#/interpreter").replace(/^#/,"");e.querySelectorAll("a").forEach(r=>{r.classList.toggle("active",r.dataset.route===t)})}async function E(e,t){try{const{body:r}=await u.health(),i=e.querySelector("[data-port]");i&&(i.textContent=String(r.port)),t.textContent="health: ok",t.classList.remove("status-err"),t.classList.add("status-ok")}catch{t.textContent="health: down",t.classList.remove("status-ok"),t.classList.add("status-err")}}function L(){const e=window.location.hash||"#/interpreter";return e.startsWith("#")?e.slice(1):e}function x(e,t){const r=n=>({route:e.find(c=>c.path===n)??null,path:n});return{routes:e,fallback:t,mount(n){const a=async()=>{const{route:c,path:d}=r(L());await(c?.render??t)(n,d)};window.addEventListener("hashchange",a),window.location.hash||(window.location.hash="#/interpreter"),a()}}}const N=`Entity Game {
  kind: Game
  characteristics: { round: 1, first_player: Unbound }
  abilities: []
}

Entity Player[i] for i ∈ {1, 2} {
  kind: Player
  characteristics: {
    aether_cap_schedule: [3, 4, 5, 6, 7, 8, 9, 10, 10, 10],
    max_mulligans: 2
  }
  counters: { aether: 0, debt: 0 }
  zones: {
    Arsenal: Zone(order: sequential),
    Hand:    Zone(capacity: 10, order: unordered),
    Cache:   Zone(order: FIFO),
    ResonanceField: Zone(capacity: 5, order: FIFO),
    Void:    Zone(order: unordered)
  }
  abilities: []
}`;function O(e){e.innerHTML="";const t=document.createElement("div");t.className="interpreter",t.innerHTML=`
    <aside>
      <label for="src">.ccgnf source</label>
      <textarea id="src" spellcheck="false"></textarea>

      <div class="row">
        <label for="seed">seed</label>
        <input id="seed" type="number" value="42" style="width: 70px" />
        <label for="inputs">inputs</label>
        <input id="inputs" type="text" value="pass,pass,pass,pass" style="flex: 1; min-width: 120px" />
      </div>

      <div class="row" data-row="stages">
        <button data-action="health">Health</button>
        <button data-action="preprocess">Preprocess</button>
        <button data-action="parse">Parse</button>
        <button data-action="ast">AST</button>
        <button data-action="validate">Validate</button>
        <button class="primary" data-action="run">Run</button>
      </div>

      <div class="row">
        <button data-action="createSession">Create session</button>
        <button data-action="listSessions">List sessions</button>
      </div>
    </aside>

    <main>
      <div class="stage">
        <h2>Output <span data-status></span></h2>
        <pre data-out>idle</pre>
      </div>
    </main>
  `,e.appendChild(t);const r=t.querySelector("#src"),i=t.querySelector("#seed"),n=t.querySelector("#inputs"),a=t.querySelector("[data-status]"),c=t.querySelector("[data-out]");r.value=N;const d=(o,s)=>{a.textContent=s?"ok":"error",a.className=s?"status-ok":"status-err",c.textContent=typeof o=="string"?o:JSON.stringify(o,null,2)},l=()=>[{path:"playground.ccgnf",content:r.value}],p=()=>n.value.split(",").map(o=>o.trim()).filter(Boolean),h={health:async()=>{const{ok:o,body:s}=await u.health();d(s,o)},preprocess:async()=>y("preprocess"),parse:async()=>y("parse"),ast:async()=>y("ast"),validate:async()=>y("validate"),run:async()=>{const{ok:o,body:s}=await u.run({files:l(),seed:parseInt(i.value,10),inputs:p()});d(s,o&&s.ok!==!1)},createSession:async()=>{const{ok:o,body:s}=await u.createSession({files:l(),seed:parseInt(i.value,10),inputs:p()});d(s,o)},listSessions:async()=>{const{ok:o,body:s}=await u.listSessions();d(s,o)}};async function y(o){const s={files:l()},{ok:w,body:m}=o==="preprocess"?await u.preprocess(s):o==="parse"?await u.parse(s):o==="ast"?await u.ast(s):await u.validate(s);d(m,w&&m.ok!==!1)}t.addEventListener("click",async o=>{const s=o.target;if(!s||s.tagName!=="BUTTON")return;const w=s.getAttribute("data-action");if(!w)return;const m=h[w];if(m)try{await m()}catch(S){d(String(S),!1)}})}const g=document.getElementById("app");if(!g)throw new Error("Missing #app root");const k=C(g,[{label:"Interpreter",href:"#/interpreter"}]),q=x([{path:"/interpreter",render:e=>O(e)}],(e,t)=>{e.innerHTML=`<main style="padding: 24px"><p class="muted">No route for <code>${t}</code></p></main>`});q.mount(k);
