// Original procedural room: a camera/composition study, not final environment art.
(() => {
 const T = exports, host = document.querySelector('.background');
 let renderer;
 try { renderer = new T.WebGLRenderer({antialias:true}); } catch { document.body.dataset.scene='fallback'; return; }
 renderer.setSize(1600,900); renderer.setPixelRatio(Math.min(devicePixelRatio,1.5));
 renderer.outputColorSpace=T.SRGBColorSpace; renderer.shadowMap.enabled=true; renderer.shadowMap.type=T.PCFSoftShadowMap;
 host.replaceChildren(renderer.domElement);
 const scene=new T.Scene(); scene.background=new T.Color('#d7ddd1'); scene.fog=new T.Fog('#cdd8d0',12,25);
 const camera=new T.PerspectiveCamera(48,16/9,.1,45);
 const mats={green:new T.MeshStandardMaterial({color:'#245a4b',roughness:.48}),wall:new T.MeshStandardMaterial({color:'#dfdfce',roughness:.9}),blue:new T.MeshStandardMaterial({color:'#234b8f',roughness:.85}),white:new T.MeshStandardMaterial({color:'#e8eee4',roughness:.9}),skin:new T.MeshStandardMaterial({color:'#bd9373',roughness:.95}),board:new T.MeshStandardMaterial({color:'#193e36',roughness:.92}),metal:new T.MeshStandardMaterial({color:'#666e69',metalness:.7,roughness:.4})};
 let seed=1999; const rand=()=>{seed=(seed*1664525+1013904223)>>>0;return seed/4294967296};
 const tile=document.createElement('canvas'); tile.width=tile.height=1024; const ctx=tile.getContext('2d');
 ctx.fillStyle='#d1d1ba';ctx.fillRect(0,0,1024,1024);
 const chips=['#8c998f','#f2eede','#b0b7a6','#667a72','#c4bca0'];
 for(let n=0;n<3100;n++){let x=rand()*1024,y=rand()*1024,r=2+rand()*8;ctx.fillStyle=chips[n%chips.length];ctx.beginPath();for(let k=0;k<5;k++){let a=k*1.256;let rr=r*(.55+rand()*.45);ctx.lineTo(x+Math.cos(a)*rr,y+Math.sin(a)*rr)}ctx.closePath();ctx.fill()}
 const tex=new T.CanvasTexture(tile);tex.colorSpace=T.SRGBColorSpace;tex.wrapS=tex.wrapT=T.RepeatWrapping;tex.repeat.set(2.2,1.6);tex.anisotropy=renderer.capabilities.getMaxAnisotropy();
 const terrazzo=new T.MeshStandardMaterial({map:tex,roughness:.72,color:'#ffffff'});
 function box(w,h,d,x,y,z,mat,parent=scene){const o=new T.Mesh(new T.BoxGeometry(w,h,d),mat);o.position.set(x,y,z);o.castShadow=true;o.receiveShadow=true;parent.add(o);return o}
 box(6.4,.16,4.2,0,1.3,0,terrazzo);
 box(6.65,.22,.13,0,1.31,-2.14,mats.green);box(6.65,.22,.13,0,1.31,2.14,mats.green);
 box(.13,.22,4.3,-3.28,1.31,0,mats.green);box(.13,.22,4.3,3.28,1.31,0,mats.green);
 for(const x of [-2.7,2.7])for(const z of [-1.6,1.6])box(.12,1.25,.12,x,.62,z,mats.metal);
 box(18,.15,18,0,-.15,-3,new T.MeshStandardMaterial({color:'#9ca99b',roughness:1}));
 box(16,6,.2,0,2.8,-6,mats.wall);box(16,1.6,.21,0,.65,-5.86,mats.green);
 box(7.5,2.9,.14,0,3.1,-5.7,mats.green);box(7.15,2.55,.18,0,3.1,-5.58,mats.board);
 box(7.55,.08,.32,0,1.62,-5.48,mats.metal);
 // Light openings: few forms, no nostalgic prop pile.
 for(const z of [-3,1,5]){box(.15,3.6,2.7,-6,3,z,mats.green);box(.17,3.32,2.43,-5.9,3,z,new T.MeshBasicMaterial({color:'#ecf4e7'}));box(.23,.07,2.5,-5.75,3,z,mats.green);box(.23,3.35,.07,-5.75,3,z,mats.green)}
 const book=box(.75,.035,1.0,-2,1.42,.9,mats.green);book.rotation.y=-.15;
 box(.65,.008,.88,-2,1.442,.9,new T.MeshStandardMaterial({color:'#a3b8a1',roughness:.9}));
 // Localized uniform torso and arms; faces intentionally outside this composition.
 const friend=new T.Group();scene.add(friend);friend.position.set(1.65,0,-2.9);
 box(.88,1.05,.46,0,1.45,0,mats.white,friend);box(.19,1.12,.48,-.38,1.43,0,mats.blue,friend);box(.19,1.12,.48,.38,1.43,0,mats.blue,friend);
 const sleeve1=box(.27,.72,.3,-.64,1.6,.17,mats.blue,friend);sleeve1.rotation.z=-.48;
 const sleeve2=box(.27,.72,.3,.62,1.6,.17,mats.blue,friend);sleeve2.rotation.z=.5;
 box(.19,.14,.55,-.65,1.46,.57,mats.skin,friend);box(.19,.14,.55,.64,1.46,.57,mats.skin,friend);
 scene.add(new T.HemisphereLight('#eff8ed','#667b69',2.5));
 const sun=new T.DirectionalLight('#fff0c6',3.2);sun.position.set(-5,8,2);sun.castShadow=true;sun.shadow.mapSize.set(2048,2048);Object.assign(sun.shadow.camera,{left:-9,right:9,top:9,bottom:-9});sun.shadow.bias=-.0003;scene.add(sun);
 const poses={boot:{p:[6.6,5.7,7.6],t:[0,1.3,-.5]},title:{p:[4.8,4.2,6.1],t:[.25,1.5,-.6]},menu:{p:[.0,9.4,1.4],t:[0,1.3,0]},settings:{p:[1.2,2.4,3.2],t:[0,3.15,-5.7]},duel:{p:[0,6.5,.8],t:[0,1.3,0]}};
 let current='title',hover=0,dirty=true,progress=1,last=performance.now(),draws=0;
 let originP=new T.Vector3(...poses.title.p),originT=new T.Vector3(...poses.title.t),targetP=originP.clone(),targetT=originT.clone(),look=originT.clone();camera.position.copy(originP);
 const reduced=()=>document.querySelector('#stage').classList.contains('reduced');
 window.menuScene={setPage(next){current=poses[next]?next:'menu';originP.copy(camera.position);originT.copy(look);targetP.set(...poses[current].p);targetT.set(...poses[current].t);progress=reduced()?1:0;hover=0;dirty=true},select(i){hover=current==='menu'?(i-1)*.09:0;dirty=true},snapshot(){return {page:current,frames:draws,position:camera.position.toArray(),target:look.toArray(),progress}}};
 function frame(now){const dt=Math.min((now-last)/1000,.05);last=now;const moving=progress<1;progress=Math.min(1,progress+dt/1.05);if(reduced())progress=1;
 if(moving||dirty){const e=1-Math.pow(1-progress,3);camera.position.lerpVectors(originP,targetP,e);look.lerpVectors(originT,targetT,e);camera.position.x+=reduced()?0:hover;camera.lookAt(look);renderer.render(scene,camera);draws++;dirty=false;document.body.dataset.scene='ready'}
 requestAnimationFrame(frame)}requestAnimationFrame(frame);
})();
