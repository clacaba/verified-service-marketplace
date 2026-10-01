import type { NextRequest } from "next/server";
export const runtime = "nodejs";
const permitted = new Set(["GET auth/csrf","POST auth/register","POST auth/login","POST auth/logout","POST auth/verify-email","POST auth/resend-verification","GET users/me","GET categories"]);
async function forward(request: NextRequest, context: { params: Promise<{ path: string[] }> }) {
 const {path}=await context.params;const route=path.join("/");
 const organizationRoute = (request.method==="POST"&&route==="organizations") || (request.method==="POST"&&route==="invitations/accept") || (request.method==="GET"&&/^organizations\/[0-9a-f-]{36}(\/members)?$/i.test(route)) || (request.method==="POST"&&/^organizations\/[0-9a-f-]{36}\/invitations$/i.test(route)) || (request.method==="DELETE"&&/^organizations\/[0-9a-f-]{36}\/members\/[0-9a-f-]{36}$/i.test(route));
 const profileRoute = ["GET", "PUT"].includes(request.method) && /^organizations\/[0-9a-f-]{36}\/provider-profile$/i.test(route);
 if(!permitted.has(request.method+" "+route)&&!organizationRoute&&!profileRoute)return Response.json({title:"Resource unavailable"},{status:404});
 if(request.method!=="GET"&&request.headers.get("origin")!==(process.env.WEB_ORIGIN??"http://localhost:3000"))return Response.json({title:"Invalid request origin"},{status:403});
 const headers=new Headers();for(const name of ["cookie","content-type","x-csrf-token","if-match"]){const value=request.headers.get(name);if(value)headers.set(name,value);}
 try{const upstream=await fetch(new URL("/api/v1/"+route+request.nextUrl.search,process.env.API_BASE_URL??"http://localhost:5080"),{method:request.method,headers,body:request.method==="GET"?undefined:await request.text(),cache:"no-store",redirect:"manual",signal:AbortSignal.timeout(10000)});const responseHeaders=new Headers({"Cache-Control":"no-store"});for(const name of ["content-type","retry-after","etag"]){const value=upstream.headers.get(name);if(value)responseHeaders.set(name,value);}for(const cookie of upstream.headers.getSetCookie())responseHeaders.append("Set-Cookie",cookie);return new Response(upstream.status===204?null:await upstream.text(),{status:upstream.status,headers:responseHeaders});}catch{return Response.json({title:"The service is temporarily unavailable."},{status:503});}
}
export const GET=forward;export const POST=forward;export const DELETE=forward;export const PUT=forward;
