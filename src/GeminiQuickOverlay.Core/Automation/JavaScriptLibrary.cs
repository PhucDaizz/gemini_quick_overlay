using System.Text.Json;

namespace GeminiQuickOverlay.Core.Automation;

internal static class JavaScriptLibrary
{
    public static string Probe(string selector)
    {
        var selectorJson = JsonSerializer.Serialize(selector);
        return $$"""
            (function(){
              const all=Array.from(document.querySelectorAll({{selectorJson}}));
              const vis=all.filter(e=>e.getClientRects().length);
              const n=vis.length?vis:all;
              if(!n.length)return{count:0,text:''};
              const e=n[n.length-1];
              return{count:n.length,text:(e.innerText||'').trim()};
            })()
            """;
    }

    public static string Focus(string selector)
    {
        var selectorJson = JsonSerializer.Serialize(selector);
        return $$"""
            (function(){
              const e=document.querySelector({{selectorJson}});
              if(!e)return false;
              e.focus();
              const s=window.getSelection();
              const r=document.createRange();
              r.selectNodeContents(e);
              s.removeAllRanges();
              s.addRange(r);
              return true;
            })()
            """;
    }

    public static string InputText(string selector)
    {
        var selectorJson = JsonSerializer.Serialize(selector);
        return $$"""
            (function(){
              const e=document.querySelector({{selectorJson}});
              return e?(e.innerText||''):'';
            })()
            """;
    }

    public static string InsertFallback(string selector, string text)
    {
        var selectorJson = JsonSerializer.Serialize(selector);
        var textJson = JsonSerializer.Serialize(text);
        return $$"""
            (function(){
              const e=document.querySelector({{selectorJson}});
              if(!e)return false;
              e.focus();
              document.execCommand('insertText',false,{{textJson}});
              return true;
            })()
            """;
    }

    public static string Send(string selector)
    {
        var selectorJson = JsonSerializer.Serialize(selector);
        return $$"""
            (function(){
              const b=document.querySelector({{selectorJson}});
              if(b&&!b.disabled){b.click();return true;}
              return false;
            })()
            """;
    }

    public static string SendReady(string selector)
    {
        var selectorJson = JsonSerializer.Serialize(selector);
        return $$"""
            (function(){
              const b=document.querySelector({{selectorJson}});
              return !!(b&&!b.disabled);
            })()
            """;
    }

    public static string Status(string inputSelector, string sendSelector)
    {
        var inputJson = JsonSerializer.Serialize(inputSelector);
        var sendJson = JsonSerializer.Serialize(sendSelector);
        return $$"""
            (function(){
              const input=document.querySelector({{inputJson}});
              const send=document.querySelector({{sendJson}});
              const url=location.href;
              const signedOut=/accounts\.google\.com|servicelogin/i.test(url);
              return{url:url,input:!!input,send:!!send,signedOut:signedOut};
            })()
            """;
    }

    // Baseline for paste detection: matching thumbnails plus total DOM nodes. The node count is a
    // selector-independent fallback signal, since Gemini's thumbnail markup changes over time.
    public static string PasteBaseline(string selector)
    {
        var selectorJson = JsonSerializer.Serialize(selector);
        return $"({{preview:document.querySelectorAll({selectorJson}).length,nodes:document.getElementsByTagName('*').length}})";
    }

    public static string WaitPaste(string selector, int initialPreview, int initialNodes, int minNewNodes, int timeoutMs)
    {
        var selectorJson = JsonSerializer.Serialize(selector);
        return $$"""
            new Promise((resolve)=>{
              const end=Date.now()+{{timeoutMs}};
              const id=setInterval(()=>{
                const grew=document.querySelectorAll({{selectorJson}}).length>{{initialPreview}}
                  ||document.getElementsByTagName('*').length>={{initialNodes}}+{{minNewNodes}};
                if(grew){
                  clearInterval(id);resolve(true);
                }else if(Date.now()>end){
                  clearInterval(id);resolve(false);
                }
              },200);
            })
            """;
    }

    public static string SyntheticPaste(string selector, string base64Png)
    {
        var selectorJson = JsonSerializer.Serialize(selector);
        var base64Json = JsonSerializer.Serialize(base64Png);
        return $$"""
            (function(){
              const e=document.querySelector({{selectorJson}});
              if(!e)return false;
              const raw=atob({{base64Json}});
              const bytes=new Uint8Array(raw.length);
              for(let i=0;i<raw.length;i++)bytes[i]=raw.charCodeAt(i);
              const f=new File([bytes],'clipboard.png',{type:'image/png'});
              const dt=new DataTransfer();
              dt.items.add(f);
              e.focus();
              e.dispatchEvent(new ClipboardEvent('paste',{bubbles:true,cancelable:true,clipboardData:dt}));
              return true;
            })()
            """;
    }

    public static string Observer(
        string responseSelector,
        int initialCount,
        string initialText,
        int settleMs,
        int timeoutMs)
    {
        var config = JsonSerializer.Serialize(new
        {
            response = responseSelector,
            initialCount,
            initialText,
            settleMs,
            timeoutMs,
            tickMs = 150,
        });

        return $$$"""
            new Promise((resolve)=>{
              if(window.__geminiObserver){try{window.__geminiObserver.disconnect();}catch(e){}}
              if(window.__geminiTimer){clearInterval(window.__geminiTimer);}
              const cfg={{{config}}};
              const started=Date.now();
              let lastText='';
              let stableSince=0;
              let done=false;
              let observer=null;
              function current(){
                const all=Array.from(document.querySelectorAll(cfg.response));
                const vis=all.filter(e=>e.getClientRects().length);
                const nodes=vis.length?vis:all;
                if(!nodes.length)return{count:0,text:''};
                const el=nodes[nodes.length-1];
                return{count:nodes.length,text:(el.innerText||'').trim()};
              }
              function finish(payload){
                if(done)return;
                done=true;
                try{if(observer)observer.disconnect();}catch(e){}
                if(window.__geminiTimer){clearInterval(window.__geminiTimer);window.__geminiTimer=null;}
                window.__geminiObserver=null;
                resolve(payload);
              }
              function tick(){
                if(done)return;
                if(window.__geminiCancel)return finish({status:'cancelled',count:0,text:''});
                const cur=current();
                const isNew=cur.count>cfg.initialCount||(cur.text&&cur.text!==cfg.initialText);
                if(isNew&&cur.text){
                  if(cur.text!==lastText){
                    lastText=cur.text;
                    stableSince=Date.now();
                  }else if(Date.now()-stableSince>=cfg.settleMs){
                    return finish({status:'ok',count:cur.count,text:cur.text});
                  }
                }
                if(Date.now()-started>cfg.timeoutMs){
                  return finish({status:'timeout',count:cur.count,text:cur.text});
                }
              }
              let scheduled=false;
              function schedule(){
                if(scheduled)return;
                scheduled=true;
                setTimeout(()=>{scheduled=false;tick();},100);
              }
              observer=new MutationObserver(schedule);
              observer.observe(document.body,{subtree:true,childList:true,characterData:true});
              window.__geminiObserver=observer;
              window.__geminiTimer=setInterval(tick,250);
              tick();
            })
            """;
    }
}
