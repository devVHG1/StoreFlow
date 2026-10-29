const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const web=path.join(__dirname,'../StoreFlow.Аpi/wwwroot');
function setup(file,stored={}){
 const nodes=new Map();
 function node(){return {value:'',options:[],dataset:{},classList:{contains:()=>false,add(){},remove(){},toggle(){}},replaceChildren(){},append(){},setAttribute(){},close(){},showModal(){},querySelector(){return node();}};}
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
