var WebSocketLibrary =
{
    $webSocketManager:
    {
        /*
         * Map of instances
         *
         * Instance structure:
         * {
         *     url: string,
         *     ws: WebSocket,
         *     subProtocols: string[],
         * }
         */
        instances: {},

        /* Last instance ID */
        lastId: 0,

        /* Event listeners */
        onOpen: null,
        onMessage: null,
        onMessageStr: null,
        onError: null,
        onClose: null,

        /* Support Unity 6000 */
        support6000: false
    },

    /**
     * Support Unity 6000
     *
     */
    WebSocketSetSupport6000: function()
    {
        webSocketManager.support6000 = true;
    },

    /**
     * Set onOpen callback
     *
     * @param callback Reference to C# static function
     */
    WebSocketSetOnOpen: function(callback)
    {
        webSocketManager.onOpen = callback;
    },

    /**
     * Set onMessage callback
     *
     * @param callback Reference to C# static function
     */
    WebSocketSetOnMessage: function(callback)
    {
        webSocketManager.onMessage = callback;
    },

    /**
     * Set onMessageStr callback
     *
     * @param callback Reference to C# static function
     */
    WebSocketSetOnMessageStr: function(callback)
    {
        webSocketManager.onMessageStr = callback;
    },

    /**
     * Set onError callback
     *
     * @param callback Reference to C# static function
     */
    WebSocketSetOnError: function(callback)
    {
        webSocketManager.onError = callback;
    },

    /**
     * Set onClose callback
     *
     * @param callback Reference to C# static function
     */
    WebSocketSetOnClose: function(callback)
    {
        webSocketManager.onClose = callback;
    },

    /**
     * Allocate new WebSocket instance struct
     *
     * @param url Server URL
     */
    WebSocketAllocate: function(urlPtr)
    {
        var url = UTF8ToString(urlPtr);
        var id = ++webSocketManager.lastId;
        webSocketManager.instances[id] = {
            url: url,
            ws: null,
        };

        return id;
    },

    /**
     * Add Sub Protocol
     *
     * @param instanceId Instance ID
     * @param protocol Sub Protocol
     */
    WebSocketAddSubProtocol: function(instanceId, protocolPtr)
    {
        var instance = webSocketManager.instances[instanceId];
        if (!instance) return -1;

        var protocol = UTF8ToString(protocolPtr);
        
        if (instance.subProtocols == null) 
            instance.subProtocols = []; 

        instance.subProtocols.push(protocol);

        return 0;
    },

    /**
     * Remove reference to WebSocket instance
     *
     * If socket is not closed function will close it but onClose event will not be emitted because
     * this function should be invoked by C# WebSocket destructor.
     *
     * @param instanceId Instance ID
     */
    WebSocketFree: function(instanceId)
    {
        var instance = webSocketManager.instances[instanceId];
        if (!instance) return 0;

        // Close if not closed
        if (instance.ws !== null && instance.ws.readyState < 2)
            instance.ws.close();

        // Remove reference
        delete webSocketManager.instances[instanceId];

        return 0;
    },
    
    /**
     * Connect WebSocket to the server
     *
     * @param instanceId Instance ID
     */
    WebSocketConnect: function(instanceId)
    {
        var instance = webSocketManager.instances[instanceId];
        if (!instance) return -1;
        if (instance.ws !== null) return -2;

        if (instance.subProtocols != null)
            instance.ws = new WebSocket(instance.url, instance.subProtocols);
        else
            instance.ws = new WebSocket(instance.url);

        instance.ws.onopen = function()
        {
            if (webSocketManager.support6000)
            {
                {{{ makeDynCall('vi', 'webSocketManager.onOpen') }}}(instanceId);
            }
            else
            {
                Module.dynCall_vi(webSocketManager.onOpen, instanceId);
            }
        };

        instance.ws.onmessage = function(ev)
        {
            if (ev.data instanceof ArrayBuffer)
            {
                var array = new Uint8Array(ev.data);
                var buffer = _malloc(array.length);
                writeArrayToMemory(array, buffer);
                try
                {
                    if (webSocketManager.support6000)
                    {
                        {{{ makeDynCall('viii', 'webSocketManager.onMessage') }}}(instanceId, buffer, array.length);
                    }
                    else
                    {
                        Module.dynCall_viii(webSocketManager.onMessage, instanceId, buffer, array.length);
                    }
                }
                finally
                {
                    _free(buffer);
                }
            }
            else if (typeof ev.data == 'string')
            {
                var length = lengthBytesUTF8(ev.data) + 1;
                var buffer = _malloc(length);
                stringToUTF8(ev.data, buffer, length);
                try
                {
                    if (webSocketManager.support6000)
                    {
                        {{{ makeDynCall('vii', 'webSocketManager.onMessageStr') }}}(instanceId, buffer);
                    }
                    else
                    {
                        Module.dynCall_vii(webSocketManager.onMessageStr, instanceId, buffer);
                    }
                }
                finally
                {
                    _free(buffer);
                }
            }
            else if (typeof Blob !== 'undefined' && ev.data instanceof Blob)
            {
                var reader = new FileReader();
                reader.onload = function()
                {
                    var array = new Uint8Array(reader.result);
                    var buffer = _malloc(array.length);
                    writeArrayToMemory(array, buffer);
                    try
                    {
                        if (webSocketManager.support6000)
                        {
                            {{{ makeDynCall('viii', 'webSocketManager.onMessage') }}}(instanceId, buffer, array.length);
                        }
                        else
                        {
                            Module.dynCall_viii(webSocketManager.onMessage, instanceId, buffer, array.length);
                        }
                    }
                    finally
                    {
                        reader = null;
                        _free(buffer);
                    }
                };
                reader.readAsArrayBuffer(ev.data);
            }
            else
            {
                console.log("[JSLIB WebSocket] not support message type: ", (typeof ev.data));
            }
        };

        instance.ws.onerror = function(ev)
        {
            var msg = "WebSocket error.";
            var length = lengthBytesUTF8(msg) + 1;
            var buffer = _malloc(length);
            stringToUTF8(msg, buffer, length);
            try
            {
                if (webSocketManager.support6000)
                {
                    {{{ makeDynCall('vii', 'webSocketManager.onError') }}}(instanceId, buffer);
                }
                else
                {
                    Module.dynCall_vii(webSocketManager.onError, instanceId, buffer);
                }
            }
            finally
            {
                _free(buffer);
            }
        };

        instance.ws.onclose = function(ev)
        {
            var msg = ev.reason;
            var length = lengthBytesUTF8(msg) + 1;
            var buffer = _malloc(length);
            stringToUTF8(msg, buffer, length);
            try
            {
                if (webSocketManager.support6000)
                {
                    {{{ makeDynCall('viii', 'webSocketManager.onClose') }}}(instanceId, ev.code, buffer);
                }
                else
                {
                    Module.dynCall_viii(webSocketManager.onClose, instanceId, ev.code, buffer);
                }
            }
            finally
            {
                _free(buffer);
            }
            instance.ws = null;
        };

        return 0;
    },

    /**
     * Close WebSocket connection
     *
     * @param instanceId Instance ID
     * @param code Close status code
     * @param reasonPtr Pointer to reason string
     */
    WebSocketClose: function(instanceId, code, reasonPtr)
    {
        var instance = webSocketManager.instances[instanceId];
        if (!instance) return -1;
        if (instance.ws === null) return -3;
        if (instance.ws.readyState === 2) return -4;
        if (instance.ws.readyState === 3) return -5;

        var reason = ( reasonPtr ? UTF8ToString(reasonPtr) : undefined );
        try
        {
            instance.ws.close(code, reason);
        }
        catch (err)
        {
            return -7;
        }

        return 0;
    },

    /**
     * Send message over WebSocket
     *
     * @param instanceId Instance ID
     * @param bufferPtr Pointer to the message buffer
     * @param offset The offset of the message in the buffer
     * @param length Length of the message in the buffer
     */
    WebSocketSend: function(instanceId, bufferPtr, offset, length)
    {
        var instance = webSocketManager.instances[instanceId];
        if (!instance) return -1;
        if (instance.ws === null) return -3;
        if (instance.ws.readyState !== 1) return -6;

        if (typeof HEAPU8 !== 'undefined')
            instance.ws.send(HEAPU8.buffer.slice(bufferPtr + offset, bufferPtr + length));
        else if (typeof buffer !== 'undefined')
            instance.ws.send(buffer.slice(bufferPtr + offset, bufferPtr + length));
        else
            return -8; // not support buffer slice

        return 0;
    },

    /**
     * Send message string over WebSocket
     *
     * @param instanceId Instance ID
     * @param stringPtr Pointer to the message string
     */
    WebSocketSendStr: function(instanceId, stringPtr)
    {
        var instance = webSocketManager.instances[instanceId];
        if (!instance) return -1;
        if (instance.ws === null) return -3;
        if (instance.ws.readyState !== 1) return -6;

        instance.ws.send(UTF8ToString(stringPtr));

        return 0;
    },

    /**
     * Return WebSocket readyState
     *
     * @param instanceId Instance ID
     */
    WebSocketGetState: function(instanceId)
    {
        var instance = webSocketManager.instances[instanceId];
        if (!instance) return -1;
        if (instance.ws === null) return 3; // socket null as closed
        
        return instance.ws.readyState;
    }
};

autoAddDeps(WebSocketLibrary, '$webSocketManager');
mergeInto(LibraryManager.library, WebSocketLibrary);
