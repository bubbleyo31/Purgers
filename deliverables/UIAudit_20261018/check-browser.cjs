const {chromium}=require('playwright');
(async()=>{const b=await chromium.launch({headless:true});console.log(await b.version());await b.close()})().catch(e=>{console.error(e.message);process.exit(1)});
