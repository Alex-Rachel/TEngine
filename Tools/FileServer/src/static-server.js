const http = require('http');
const https = require('https');
const path = require('path');
const fs = require('fs');
const url = require('url');
const zlib = require('zlib');
const chalk = require('chalk');
const os = require('os');
const open = require("open");
const Handlebars = require('handlebars');
const pem = require('pem');
const mime = require('./mime');
const Template = require('./templates');

const _defaultTemplate = Handlebars.compile(Template.page_dafault);
const _404TempLate = Handlebars.compile(Template.page_404);

const hasTrailingSlash = url => url[url.length - 1] === '/';

const ifaces = os.networkInterfaces();

class StaticServer {
    constructor(options) {
        this.port = options.port;
        this.indexPage = options.index;
        this.openIndexPage = options.openindex;
        this.openBrowser = options.openbrowser;
        this.charset = options.charset;
        this.cors = options.cors;
        this.protocal = options.https ? 'https' : 'http';
        this.zipMatch = '^\\.(css|js|html)$';
    }

    /**
     * 响应错误
     *
     * @param {*} err
     * @param {*} res
     * @returns
     * @memberof StaticServer
     */
    respondError(err, res) {
        res.writeHead(500);
        return res.end(err);        
    }

    /**
     * 响应404
     *
     * @param {*} req
     * @param {*} res
     * @memberof StaticServer
     */
    respondNotFound(req, res) {
        res.writeHead(404, {
            'Content-Type': 'text/html'
        });
        const html = _404TempLate();
        res.end(html);
    }

    respond(pathName, req, res) {
        fs.stat(pathName, (err, stat) => {
            if (err) return respondError(err, res);
            this.responseFile(stat, pathName, req, res);
        });
    }

    /**
     * 判断是否需要解压
     *
     * @param {*} pathName
     * @returns
     * @memberof StaticServer
     */
    shouldCompress(pathName) {
        return path.extname(pathName).match(this.zipMatch);
    }

    /**
     * 解压文件
     *
     * @param {*} readStream
     * @param {*} req
     * @param {*} res
     * @returns
     * @memberof StaticServer
     */
    compressHandler(readStream, req, res) {
        const acceptEncoding = req.headers['accept-encoding'];
        if (!acceptEncoding || !acceptEncoding.match(/\b(gzip|deflate)\b/)) {
            return readStream;
        } else if (acceptEncoding.match(/\bgzip\b/)) {
            res.setHeader('Content-Encoding', 'gzip');
            return readStream.pipe(zlib.createGzip());
        }
    }

    /**
     * 响应文件路径
     *
     * @param {*} stat
     * @param {*} pathName
     * @param {*} req
     * @param {*} res
     * @memberof StaticServer
     */
    responseFile(stat, pathName, req, res) {
        // 设置响应头
        res.setHeader('Content-Type', `${mime.lookup(pathName)}; charset=${this.charset}`);
        res.setHeader('Accept-Ranges', 'bytes');

        // 添加跨域
        if (this.cors) res.setHeader('Access-Control-Allow-Origin', '*');

        let readStream;
        readStream = fs.createReadStream(pathName);
        if (this.shouldCompress(pathName)) { // 判断是否需要解压
            readStream = this.compressHandler(readStream, req, res);
        }
        readStream.pipe(res);
    }
  
    /**
     * 响应重定向
     *
     * @param {*} req
     * @param {*} res
     * @memberof StaticServer
     */
    respondRedirect(req, res) {
        const location = req.url + '/';
        res.writeHead(301, {
            'Location': location,
            'Content-Type': 'text/html'
        });
        const html = _defaultTemplate({
            htmlStr: `Redirecting to <a href='${location}'>${location}</a>`,
            showFileList: false
        })
        res.end(html);
    }

    /**
     * 响应文件夹路径
     *
     * @param {*} pathName
     * @param {*} req
     * @param {*} res
     * @memberof StaticServer
     */
    respondDirectory(pathName, req, res) {
        const indexPagePath = path.join(pathName, this.indexPage);
        // 如果文件夹下存在index.html，则默认打开
        if (this.openIndexPage && fs.existsSync(indexPagePath)) {
            this.respond(indexPagePath, req, res);
        } else {
            fs.readdir(pathName, (err, files) => {
                if (err) {
                    respondError(err, res);
                }
                const requestPath = url.parse(req.url).pathname;
                const fileList = [];
                files.forEach(fileName => {
                    let itemLink = path.join(requestPath, fileName);
                    let isDirectory = false;
                    const stat = fs.statSync(path.join(pathName, fileName));
                    if (stat && stat.isDirectory()) {
                        itemLink = path.join(itemLink, '/');
                        isDirectory = true;
                    }     
                    fileList.push({
                        link: itemLink,
                        name: fileName,
                        isDirectory
                    });            
                });
                // 排序，目录在前，文件在后
                fileList.sort((prev, next) => {
                    if (prev.isDirectory && !next.isDirectory) {
                        return -1;
                    }
                    return 1;
                });
                res.writeHead(200, {
                    'Content-Type': 'text/html'
                });
                const html = _defaultTemplate({
                    requestPath,
                    fileList,
                    showFileList: true
                })
                res.end(html);
            });
        }
    }

    /**
     * 路由处理
     *
     * @param {*} pathName
     * @param {*} req
     * @param {*} res
     * @memberof StaticServer
     */
    routeHandler(pathName, req, res) {
        const realPathName = pathName.split('?')[0];
        fs.stat(realPathName, (err, stat) => {
            this.logGetInfo(err, pathName);
            if (!err) {
                const requestedPath = url.parse(req.url).pathname;
                // 检查url
                // 如果末尾有'/'，且是文件夹，则读取文件夹
                // 如果是文件夹，但末尾没'/'，则重定向至'xxx/'
                // 如果是文件，则判断是否是压缩文件，是则解压，不是则读取文件
                if (hasTrailingSlash(requestedPath) && stat.isDirectory()) {
                    this.respondDirectory(realPathName, req, res);
                } else if (stat.isDirectory()) {
                    this.respondRedirect(req, res);
                } else {
                    this.respond(realPathName, req, res);
                }
            } else {
                this.respondNotFound(req, res);
            }
        });
    }

    /**
     * 打印ip地址
     *
     * @memberof StaticServer
     */
    logUsingPort() {
        const me = this;
        console.log(`${chalk.yellow(`Starting up your server\nAvailable on:`)}`);
        Object.keys(ifaces).forEach(function (dev) {
            ifaces[dev].forEach(function (details) {
                if (details.family === 'IPv4') {
                    console.log(`   ${me.protocal}://${details.address}:${chalk.green(me.port)}`);
                }
            });
        });
        console.log(`${chalk.cyan(Array(50).fill('-').join(''))}`);
    }

    /**
     * 打印占用端口
     *
     * @param {*} oldPort
     * @param {*} port
     * @memberof StaticServer
     */
    logUsedPort(oldPort, port) {
        const me = this;
        console.log(`${chalk.red(`The port ${oldPort} is being used, change to port `)}${chalk.green(me.port)} `);
    }

    /**
     * 打印https证书友好提示
     *
     * @memberof StaticServer
     */
    logHttpsTrusted() {
        console.log(chalk.green('Currently is using HTTPS certificate (Manually trust it if necessary)'));
    }


    /**
     * 打印路由路径输出
     *
     * @param {*} isError
     * @param {*} pathName
     * @memberof StaticServer
     */
    logGetInfo(isError, pathName) {
        if (isError) {
            console.log(chalk.red(`404  ${pathName}`));
        } else {
            console.log(chalk.cyan(`200  ${pathName}`));
        }
    }

    startServer(keys) {
        const me = this;
        let isPostBeUsed = false;
        const oldPort = me.port;
        const protocal = me.protocal === 'https' ? https : http;
        const options = me.protocal === 'https' ? { key: keys.serviceKey, cert: keys.certificate } : null;
        const callback = (req, res) => {
            const pathName = path.join(process.cwd(), path.normalize(decodeURI(req.url)));
            me.routeHandler(pathName, req, res);
        };
        const params = [callback];
        if (me.protocal === 'https') params.unshift(options);
        const server = protocal.createServer(...params).listen(me.port);
        server.on('listening', function () { // 执行这块代码说明端口未被占用
            if (isPostBeUsed) {
                me.logUsedPort(oldPort, me.port);
            }
            me.logUsingPort();
            if (me.openBrowser) {
                open(`${me.protocal}://127.0.0.1:${me.port}`);
            }
        });
        
        server.on('error', function (err) {
            if (err.code === 'EADDRINUSE') { // 端口已经被使用
                isPostBeUsed = true;
                me.port = parseInt(me.port) + 1;
                server.listen(me.port);
            } else {
                console.log(err);
            }
        })
    }

    start() {
        const me = this;
        if (this.protocal === 'https') {
            pem.createCertificate({ days: 1, selfSigned: true }, function (err, keys) {
                if (err) {
                  throw err
                }
                me.logHttpsTrusted();
                me.startServer(keys);
            })
        } else {
            me.startServer();
        }
    }
}

module.exports = StaticServer;