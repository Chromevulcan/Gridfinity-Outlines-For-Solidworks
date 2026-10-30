// Mirrors outline-app's lib/export/dxf/DxfExport.ts and lib/export/svg/SvgExport.ts (same library, same calls).
import { DxfWriter, LWPolylineFlags, point2d, Units } from "@tarikjabiri/dxf";
import JsZip from "jszip";
import fs from "fs";
const data = JSON.parse(fs.readFileSync("contours.json"));
const out = process.argv[2]; fs.mkdirSync(out, { recursive: true });

const bounds = (cs) => { let a=[Infinity,Infinity,-Infinity,-Infinity];
  cs.forEach(c=>c.points.forEach(p=>{a=[Math.min(a[0],p.x),Math.min(a[1],p.y),Math.max(a[2],p.x),Math.max(a[3],p.y)]})); return a; };
const centerOnOrigin = (cs) => { const [x0,y0,x1,y1]=bounds(cs); const tx=(x0+x1)/2, ty=(y0+y1)/2;
  return cs.map(c=>({points:c.points.map(p=>({x:p.x-tx,y:p.y-ty}))})); };
const flipY = (cs) => cs.map(c=>({points:c.points.map(p=>({x:p.x,y:-p.y}))}));
const pointsOf = (o) => [o.outline, ...(o.holes||[])];

const dxfOf = (contours) => { const dxf = new DxfWriter(); dxf.setUnits(Units.Millimeters);
  contours.forEach(({points}) => dxf.addLWPolyline(points.map(it=>({point:point2d(it.x,it.y)})), {flags: LWPolylineFlags.Closed}));
  return dxf.stringify(); };
const svgOf = (contours, pd) => {
  const path = contours.map(({points}) => { let d=`M ${points[0].x} ${points[0].y}`; for (let i=1;i<points.length;i++) d+=` L ${points[i].x} ${points[i].y}`; return d+" Z "; }).join("");
  return `<svg xmlns="http://www.w3.org/2000/svg"  width="${pd.width}mm" height="${pd.height}mm"
             viewBox="${-pd.width/2} ${-pd.height/2} ${pd.width} ${pd.height}" >
      <path d="${path}" stroke-width="1" stroke="black" fill="none"/>
    </svg>`; };

const zip = new JsZip(); let i = 0;
for (const [name, outline] of Object.entries(data.contours)) {
  const pts = pointsOf(outline);
  const dxf = dxfOf(flipY(centerOnOrigin(pts)));
  fs.writeFileSync(`${out}/${name}.dxf`, dxf);
  fs.writeFileSync(`${out}/${name}.svg`, svgOf(centerOnOrigin(pts), data.paper));
  zip.file(`outline-${i++}.dxf`, dxf);
}
fs.writeFileSync(`${out}/toolset_dxf.zip`, await zip.generateAsync({ type: "nodebuffer" }));
console.log(fs.readdirSync(out));
