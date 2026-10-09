"use strict";
const site=document.getElementById("site");
const status=document.getElementById("status");
const show=document.getElementById("show");
const save=document.getElementById("save");
async function activeTab() {
  const tabs=await chrome.tabs.query({active:true,currentWindow:true});
  const tab=tabs?.[0];
  if(!tab || !tab.id || !tab.url || !tab.url.startsWith("https://"))
    throw new Error("Open an HTTPS login page in Chrome/Edge first.");
  return tab;
}
(async()=>{
  try{
    const tab=await activeTab();
    site.textContent=new URL(tab.url).host;
  }catch(e){site.textContent="HTTPS sites only";status.textContent=e.message;}
})();
async function ask(command) {
  show.disabled=true;save.disabled=true;
  status.textContent="Checking the current HTTPS page…";
  try{
    const tab=await activeTab();
    const reply=await chrome.tabs.sendMessage(tab.id,{op:command});
    if(reply?.status==="ok"){
      status.textContent="Suggestions opened next to the login field.";
      if(command==="showSuggestions")window.close();
    }else if(reply?.status==="no-fields"){
      status.textContent="No recognizable login input here. Click a username or password field, or use Save this website.";
    }else if(reply?.status==="started"){
      status.textContent="Review the prefilled login form in BoshaVault Windows.";
    }else{
      status.textContent=reply?.message||"BoshaVault did not respond.";
    }
  }catch{
    status.textContent="Reload the HTTPS page and retry. Ensure the extension is enabled for this site.";
  }finally{show.disabled=false;save.disabled=false;}
}
show.addEventListener("click",()=>ask("showSuggestions"));
save.addEventListener("click",()=>ask("captureSite"));
