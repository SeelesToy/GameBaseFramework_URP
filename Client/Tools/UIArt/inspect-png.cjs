// Read-only PNG inspection. Never changes image pixels.
const fs = require('fs');
const zlib = require('zlib');
function inspectPng(file) {
  const data = fs.readFileSync(file);
  if (!data.subarray(0, 8).equals(Buffer.from([137,80,78,71,13,10,26,10]))) throw Error('Not PNG: ' + file);
  let width, height, depth, color, interlace, compressed=[];
  for(let pos=8;pos<data.length;) {
    const len=data.readUInt32BE(pos), type=data.toString('ascii',pos+4,pos+8), body=data.subarray(pos+8,pos+8+len);
    if(type==='IHDR') { width=body.readUInt32BE(0);height=body.readUInt32BE(4);depth=body[8];color=body[9];interlace=body[12]; }
    if(type==='IDAT') compressed.push(body);
    pos+=len+12;
  }
  if(depth!==8 || ![2,6].includes(color) || interlace!==0) throw Error('Unsupported PNG encoding: '+file);
  const bpp=color===6?4:3, stride=width*bpp, raw=zlib.inflateSync(Buffer.concat(compressed));
  let prev=Buffer.alloc(stride), current=Buffer.alloc(stride), offset=0;
  let transparent=0, partial=0, opaque=0, minX=width, minY=height, maxX=-1, maxY=-1;
  let centerAlpha=255, topLeftAlpha=255;
  const paeth=(a,b,c)=>{const p=a+b-c,pa=Math.abs(p-a),pb=Math.abs(p-b),pc=Math.abs(p-c);return pa<=pb&&pa<=pc?a:pb<=pc?b:c;};
  for(let y=0;y<height;y++) {
    const filter=raw[offset++];
    for(let x=0;x<stride;x++) {
      const a=x>=bpp?current[x-bpp]:0,b=prev[x],c=x>=bpp?prev[x-bpp]:0;
      const predictor=filter===0?0:filter===1?a:filter===2?b:filter===3?Math.floor((a+b)/2):filter===4?paeth(a,b,c):NaN;
      if(Number.isNaN(predictor))throw Error('Invalid PNG filter');
      current[x]=(raw[offset++]+predictor)&255;
    }
    for(let x=0;x<width;x++) {
      const alpha=bpp===4?current[x*bpp+3]:255;
      if(alpha===0)transparent++;else if(alpha===255)opaque++;else partial++;
      if(alpha>=32){minX=Math.min(minX,x);maxX=Math.max(maxX,x);minY=Math.min(minY,y);maxY=Math.max(maxY,y);}
      if(x===0&&y===0)topLeftAlpha=alpha;
      if(x===Math.floor(width/2)&&y===Math.floor(height/2))centerAlpha=alpha;
    }
    [prev,current]=[current,prev];
  }
  return {width,height,colorType:color,bytes:data.length,transparentPixels:transparent,partialAlphaPixels:partial,opaquePixels:opaque,transparentRatio:transparent/(width*height),topLeftAlpha,centerAlpha,visibleBounds:{x:minX,y:minY,width:maxX-minX+1,height:maxY-minY+1}};
}
module.exports={inspectPng};
if(require.main===module) for(const file of process.argv.slice(2)) console.log(JSON.stringify({file,...inspectPng(file)}));
