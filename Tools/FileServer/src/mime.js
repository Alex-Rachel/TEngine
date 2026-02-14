const path = require('path');
const mime = require('mime');

const lookup = (pathName) => {
    let ext = path.extname(pathName);
    ext = ext.split('.').pop();
    return mime.getType(ext) || mime.getType('txt');
}

module.exports = {
    lookup
};
