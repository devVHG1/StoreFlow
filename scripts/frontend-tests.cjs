const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const web=path.join(__dirname,'../StoreFlow.Аpi/wwwroot');
function setup(file,stored={}){
 const nodes=new Map();
 function node(){return {children:[],value:'',options:[],dataset:{},classList:{contains:()=>false,add(){},remove(){},toggle(){}},replaceChildren(...children){this.children=[...children];},append(...children){this.children.push(...children);},setAttribute(){},close(){},showModal(){},querySelector(){return node();}};}
 const document={getElementById(id){if(!nodes.has(id))nodes.set(id,node());return nodes.get(id);},createElement:node,querySelectorAll:()=>[]};
 const storage=new Map(Object.entries(stored));
 const context=vm.createContext({document,sessionStorage:{getItem:k=>storage.get(k),setItem:(k,v)=>storage.set(k,v),removeItem:k=>storage.delete(k)},crypto:{randomUUID:()=> 'fixed-test-request'},console,URLSearchParams});
 let source=fs.readFileSync(path.join(web,file),'utf8');
 if(file==='shop.js')source=source.split('(async()=>{cartCount();identity();')[0];
 vm.runInContext(source+'\nthis.app={state,jsonPrice:typeof jsonPrice===\'function\'?jsonPrice:null};',context);
 return {context,nodes,app:context.app};
}
test('Server 500 preserves checkout key and retries the same request',async()=>{
 const {context,nodes,app}=setup('shop.js');const calls=[];
 context.fetch=async(url,options)=>{calls.push(JSON.parse(options.body));calls.at(-1).key=options.headers['Idempotency-Key'];return {ok:false,status:500,text:async()=>JSON.stringify({title:'Temporary error'})};};
 app.state.token='test-token';app.state.user={id:7};app.state.view='orders';app.state.cart.set(1,{quantity:2,product:{name:'Item',price:'0.5',stockQuantity:5}});
 await nodes.get('checkout').onclick();const key=app.state.pending.key;
 await nodes.get('checkout').onclick();
 assert.equal(app.state.pending.key,key);assert.equal(calls.length,2);assert.deepEqual(calls[0],calls[1]);assert.equal(app.state.cart.get(1).quantity,2);
});
test('Unreadable cart does not discard an unresolved checkout',()=>{
 const pending={actor:7,key:'pending-order',items:[{productId:42,quantity:3}]};
 const {app}=setup('shop.js',{'storeflow.cart':'null','storeflow.checkout':JSON.stringify(pending)});
 assert.equal(app.state.pending.key,'pending-order');assert.equal(app.state.cart.get(42).quantity,3);
});
test('Damaged checkout quantities are rejected',()=>{
 const {app}=setup('shop.js',{'storeflow.checkout':JSON.stringify({actor:7,key:'key',items:[{productId:1,quantity:2147483648}]})});assert.equal(app.state.pending,null);
});
test('Fractional prices remain valid JSON without losing decimal precision',()=>{
 const {app}=setup('panel.js');
 for(const [input,expected] of [['000.50','0.50'],['001e2','1e2'],['.5','0.5'],['1.','1'],['1.e2','1e2'],['0.1234567890123456789012345678','0.1234567890123456789012345678']]){
  assert.equal(app.jsonPrice(input),expected);assert.doesNotThrow(()=>JSON.parse('{"price":'+app.jsonPrice(input)+'}'));
 }
});

test('Order details display historical snapshot instead of renamed catalog product',async()=>{
 const {context,nodes,app}=setup('shop.js');
 app.state.user={id:7};app.state.token='test-token';app.state.products.set(1,{name:'Нова назва'});
 context.fetch=async(url)=>({ok:true,status:200,text:async()=>JSON.stringify(url.endsWith('/history')?[]:{id:1,userId:7,status:'Pending',totalAmount:10,items:[{productId:1,productName:'Історична назва',unitPrice:10,quantity:1,lineTotal:10,isNameBackfilled:false}]})});
 await context.orderDetails(1);
 assert.match(nodes.get('orderLines').children[0].textContent,/Історична назва/);
 assert.doesNotMatch(nodes.get('orderLines').children[0].textContent,/Нова назва/);
});
test('Buyer order navigation requests bounded pages for the signed-in owner',async()=>{
 const {context,nodes,app}=setup('shop.js');const routes=[];
 app.state.token='test-token';app.state.user={id:7};app.state.view='orders';
 context.fetch=async(url)=>{routes.push(url);const page=Number(new URL(url,'http://test').searchParams.get('page'));return {ok:true,status:200,text:async()=>JSON.stringify({items:[{id:page,userId:7,createdAt:'2026-10-30T00:00:00Z',status:'Pending',totalAmount:10,items:[]}],page,totalCount:11,totalPages:2,hasNextPage:page===1,hasPreviousPage:page>1})};};
 await context.orders();assert.equal(nodes.get('orderPrevious').disabled,true);assert.equal(nodes.get('orderNext').disabled,false);
 await nodes.get('orderNext').onclick();await new Promise(resolve=>setImmediate(resolve));
 const query=new URL(routes[1],'http://test').searchParams;
 assert.equal(query.get('page'),'2');assert.equal(query.get('pageSize'),'10');assert.equal(query.get('userId'),'7');assert.equal(nodes.get('orderNext').disabled,true);
});

test('Late order page responses cannot replace a newer page',async()=>{
 const {context,nodes,app}=setup('shop.js');app.state.token='test-token';app.state.user={id:7};let resolveFirst;
 context.fetch=url=>new Promise(resolve=>{const page=Number(new URL(url,'http://test').searchParams.get('page'));const response={ok:true,status:200,text:async()=>JSON.stringify({items:[],page,totalCount:20,totalPages:2,hasNextPage:page===1,hasPreviousPage:page===2})};if(page===1)resolveFirst=()=>resolve(response);else resolve(response);});
 const first=context.orders();app.state.ordersPage=2;await context.orders();resolveFirst();await first;
 assert.match(nodes.get('orderPageLabel').textContent,/Сторінка 2 з 2/);
});

test('Manager returns to the last available page after filtered records disappear',async()=>{
 const {context,nodes,app}=setup('panel.js');const routes=[];
 app.state.token='test-token';app.state.view='orders';app.state.page=2;
 context.fetch=async(url)=>{routes.push(url);const page=Number(new URL(url,'http://test').searchParams.get('page'));return {ok:true,status:200,text:async()=>JSON.stringify({items:[],page,totalCount:1,totalPages:1})};};
 await context.load();assert.equal(app.state.page,1);assert.equal(routes.length,2);assert.equal(new URL(routes[1],'http://test').searchParams.get('page'),'1');
});
