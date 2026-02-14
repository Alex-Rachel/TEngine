const StaticServer = require('../src/static-server');

const options = require('yargs')
    .option('p', { alias: 'port',  describe: '设置服务启动的端口号', type: 'number' })
    .option('i', { alias: 'index', describe: '设置默认打开的主页', type: 'string' })
    .option('c', { alias: 'charset', describe: '设置文件的默认字符集', type: 'string' })
    .option('o', { alias: 'openindex', describe: '是否打开默认页面', type: 'boolean' })
    .option('h', { alias: 'https', describe: '是否启用https服务', type: 'boolean' })
    .option('cors', { describe: '是否开启文件跨域', type: 'boolean' })
    .option('openbrowser', { describe: '是否默认打开浏览器', type: 'boolean' })

    // 默认参数
    .default('openbrowser', true)
    // .default('https', true)
    .default('port', 8080)
    .default('index', 'index.html')
    .default('openindex', 'index.html')
    .default('charset', 'UTF-8')
  
    .help()
    .alias('?', 'help')

    .argv;

const server = new StaticServer(options);
server.start();