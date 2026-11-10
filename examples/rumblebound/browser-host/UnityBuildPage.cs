public static class UnityBuildPage
{
    public const string Html="""
<!doctype html>
<html lang="zh-Hant"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Rumblebound · 尚未建置 Unity 網頁版</title>
<style>body{margin:0;background:#151820;color:#f3ecdf;font:18px/1.7 system-ui,sans-serif}main{max-width:780px;margin:10vh auto;padding:28px}h1{line-height:1.3}pre{padding:20px;background:#252a36;overflow:auto}a{color:#e7c17b}code{font-family:monospace}</style></head>
<body><main><p>RUMBLEBOUND</p><h1>尚未建置 Unity 網頁版</h1><p>完整遊戲使用 Unity 原角色、地圖、動畫、特效與介面。請在 GameNetKit 目錄開啟 PowerShell，執行：</p>
<pre><code>./examples/Build-Unity.ps1 -Project RumbleboundExample -Target Web</code></pre>
<p>建置完成後<a href="/">重新載入遊戲</a>。服務可保持開啟；建置輸出位於 <code>examples/rumblebound/client/Builds/WebGL</code>。</p>
<p><a href="/diagnostics/">開啟 Canvas 網路診斷介面</a>（簡化測試畫面）</p></main></body></html>
""";
}
