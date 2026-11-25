// Copies generated originals unchanged, creates Unity 2020 Sprite import metadata,
// and records read-only alpha/dimension checks. Run from the Unity project root.
const fs=require('fs'),path=require('path'),crypto=require('crypto');
const {inspectPng}=require('./inspect-png.cjs');
const root=process.cwd(), uiRoot=path.join(root,'Assets/GameMain/Res/UI');
const manifest=JSON.parse(fs.readFileSync(path.join(__dirname,'asset-generation.json'),'utf8'));
const guid=()=>crypto.randomBytes(16).toString('hex');
const hash=file=>crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
const borderFor=(a,s)=>{
  let f=null;
  if(a.id==='panel')f=[.11,.17,.11,.18];
  if(a.id==='status_panel')f=[.07,.38,.07,.38];
  if(a.id==='button_cyan'||a.id==='button_yellow')f=[.13,.36,.13,.36];
  if(a.id==='tab')f=[.20,.36,.20,.36];
  if(a.id==='progress_track')f=[.08,.45,.08,.45];
  return f?f.map((n,i)=>Math.round(n*(i%2?s.height:s.width))):[0,0,0,0];
};
const imported=[];
for(const asset of manifest.assets){
  if(!asset.source)throw Error('Missing source for '+asset.id);
  const target=path.resolve(uiRoot,asset.path);
  if(!target.startsWith(uiRoot+path.sep))throw Error('Invalid target: '+target);
  fs.mkdirSync(path.dirname(target),{recursive:true});
  if(fs.existsSync(target)){if(hash(target)!==hash(asset.source))throw Error('Refusing to replace existing asset: '+target);}
  else fs.copyFileSync(asset.source,target);
  const s=inspectPng(target),border=borderFor(asset,s),maxSize=asset.path.includes('/Icons/')?512:2048;
  if(asset.id!=='background'&&(s.colorType!==6||s.transparentPixels===0))throw Error('Expected real alpha: '+asset.path);
  if(asset.id==='card_frame'&&s.centerAlpha!==0)throw Error('Card artwork window is not transparent');
  const meta=target+'.meta';
  if(!fs.existsSync(meta))fs.writeFileSync(meta,`fileFormatVersion: 2
guid: ${guid()}
TextureImporter:
  fileIDToRecycleName: {}
  externalObjects: {}
  serializedVersion: 6
  mipmaps:
    mipMapMode: 0
    enableMipMap: 0
    sRGBTexture: 1
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  bumpmap:
    convertToNormalMap: 0
    externalNormalMap: 0
    heightScale: 0.25
    normalMapFilter: 0
  isReadable: 0
  streamingMipmaps: 0
  streamingMipmapsPriority: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: 1
  maxTextureSize: ${maxSize}
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    mipBias: -100
    wrapU: 1
    wrapV: 1
    wrapW: 1
  nPOTScale: 0
  lightmap: 0
  compressionQuality: 50
  spriteMode: 1
  spriteExtrude: 1
  spriteMeshType: 0
  alignment: 0
  spritePivot: {x: 0.5, y: 0.5}
  spritePixelsToUnits: 400
  spriteBorder: {x: ${border[0]}, y: ${border[1]}, z: ${border[2]}, w: ${border[3]}}
  spriteGenerateFallbackPhysicsShape: 0
  alphaUsage: 1
  alphaIsTransparency: 1
  spriteTessellationDetail: -1
  textureType: 8
  textureShape: 1
  singleChannelComponent: 0
  maxTextureSizeSet: 0
  compressionQualitySet: 0
  textureFormatSet: 0
  platformSettings:
  - serializedVersion: 2
    buildTarget: DefaultTexturePlatform
    maxTextureSize: ${maxSize}
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 0
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    androidETC2FallbackOverride: 0
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    physicsShape: []
    bones: []
    spriteID: ${guid()}
    vertices: []
    indices:
    edges: []
    weights: []
  spritePackingTag:
  userData:
  assetBundleName:
  assetBundleVariant:
`);
  let dir=path.dirname(target);
  while(dir!==uiRoot&&dir.startsWith(uiRoot+path.sep)){
    if(!fs.existsSync(dir+'.meta'))fs.writeFileSync(dir+'.meta',`fileFormatVersion: 2\nguid: ${guid()}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n`);
    dir=path.dirname(dir);
  }
  imported.push({id:asset.id,label:asset.label||asset.id,path:'Assets/GameMain/Res/UI/'+asset.path,sha256:hash(target),...s,spriteBorder:border,importMaxSize:maxSize,pixelsPerUnit:400,imageType:border.some(Boolean)?'Sliced':asset.id==='progress_fill'?'Filled / Horizontal':'Simple',bundleSuggestion:asset.path.startsWith('MainMenu/')?'ui/mainmenu':asset.path.startsWith('Battle/')?'ui/battle':asset.path.startsWith('Collection/')?'ui/collection':'ui/common'});
}
fs.mkdirSync(path.join(root,'Artifacts/UI'),{recursive:true});
fs.writeFileSync(path.join(root,'Artifacts/UI/asset-catalog.json'),JSON.stringify({generatedWith:'built-in image_gen',assetCount:imported.length,assets:imported},null,2));
console.log(JSON.stringify({assetCount:imported.length,totalBytes:imported.reduce((sum,a)=>sum+a.bytes,0),transparentAssets:imported.filter(a=>a.transparentPixels>0).length,files:imported.map(a=>({id:a.id,size:[a.width,a.height],alpha:a.transparentRatio,render:a.imageType}))},null,2));
