module.exports =  `
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8"/>
  <meta name="viewport" content="width=device-width, initial-scale=1.0"/>
  <meta http-equiv="X-UA-Compatible" content="ie=edge"/>
  <title>node静态服务器</title>
  <style>
    .not-found-content {
      display: flex;
      justify-content: center;
      min-height: 500px;
      align-items: center;
    }
    .not-found-content .img-notfound {
      margin-right: 50px;
    }
    .not-found-content h3 {
      color: #333;
      font-size: 24px;
      margin: 20px 0;
      font-weight: 400;
      line-height: 24px;
    }
    .not-found-content p {
      color: #666;
      font-size: 16px;
      line-height: 20px;
      margin-bottom: 7px;
    }
  </style>
</head>
<body>
  <div class="not-found-content">
    <img src="https://img.alicdn.com/tfs/TB1txw7bNrI8KJjy0FpXXb5hVXa-260-260.png" class="img-notfound" alt="not found">
    <div class="prompt">
      <h3>抱歉，你访问的路径不存在</h3>
      <p>您要找的页面没有找到，请返回<a class="link-font" href="/">首页</a>继续浏览</p>
    </div>
  </div>
</body>
</html>
`