module.exports = `
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <meta http-equiv="X-UA-Compatible" content="ie=edge">
  <title>node静态服务器</title>
  <style>
    html, body, ul, li, p{
      padding: 0;
      margin: 0;
    }
    html, body {
      padding: 0;
      margin: 0;
      width: 100%;
      height: 100%;
    }
    .app {
      padding: 20px 50px 0;
      min-height: calc(100% - 70px);
      overflow: hidden;
      color: #333;
    }
    .directory li {
      list-style: circle;
      margin-left: 20px;
    }
    .directory li p {
      line-height: 1.7;
      margin: 14px 0;
    }
    .directory li p a{
      color: #333;
      font-weight: 400;
      text-decoration: none;
    }
    .directory li p span{
      color: #3dcccc;
    }
    .directory li p a:hover {
      color: red;
    }
    .footer {
      text-align: center;
      height: 50px;
      font-size: 12px;
    }
    .footer span {
      display: block;
      line-height: 24px;
    }
    .footer .bold {
      font-weight: 600;
    }
    .footer a {
      color: #333;
    }
    .footer a:hover {
      color: red;
    }
  </style>
</head>
<body>
  <div class="app">
      <h3>当前目录：<span>{{requestPath}}</span></h3>
      {{#if showFileList}}
        <ul class="directory">
          {{#each fileList}}
            <li>
              <p>
                {{#if isDirectory }}
                <span>「目录」</span>
                {{else}}
                <span>「文件」</span>
                {{/if}}
                <a href='{{link}}'>{{name}}</a>
              </p>
            </li>
          {{/each}}
        </ul>
      {{else}}
        {{htmlStr}}
      {{/if}}
  </div>
  <div class="footer">
      <span>Github地址: <a target="_blank" href="https://github.com/WisestCoder/static-server">https://github.com/WisestCoder/static-server</a></span>
      <span class="bold">By <a target="_blank" href="https://github.com/WisestCoder">WisestCoder</a></span>
  </div>
</body>
</html>
`;