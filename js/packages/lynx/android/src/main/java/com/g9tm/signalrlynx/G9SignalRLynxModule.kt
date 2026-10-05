package com.g9tm.signalrlynx

import com.g9tm.signalrlynx.core.G9Envelope
import com.g9tm.signalrlynx.core.G9FileCore
import com.g9tm.signalrlynx.core.G9SocketCore
import com.g9tm.signalrlynx.generated.G9SignalRLynxModuleSpec
import com.lynx.jsbridge.LynxMethod
import com.lynx.jsbridge.LynxNativeModule
import com.lynx.react.bridge.Callback
import com.lynx.react.bridge.ReadableArray
import com.lynx.react.bridge.ReadableMap
import com.lynx.tasm.behavior.LynxContext
import java.util.concurrent.ExecutorService
import java.util.concurrent.Executors

/**
 * `NativeModules.G9SignalRLynxModule` (types/g9-signalr-lynx-module.d.ts): a binary WebSocket over OkHttp and the
 * file operations of the .NET-twin transfers, for @g9tm/signalr-supernetcore-lynx. One process-wide socket core, so
 * every LynxView of the app shares it (socket ids carry a per-context prefix). File work runs on one background
 * thread, in call order.
 */
@LynxNativeModule(name = "G9SignalRLynxModule")
class G9SignalRLynxModule(context: LynxContext) : G9SignalRLynxModuleSpec(context) {
  companion object {
    const val VERSION = "2.10.0"
    val sockets: G9SocketCore by lazy { G9SocketCore() }
    private val files: ExecutorService by lazy {
      Executors.newSingleThreadExecutor { runnable -> Thread(runnable, "g9-signalr-files").apply { isDaemon = true } }
    }
  }

  @LynxMethod
  override fun capabilities(callback: Callback?) =
    G9Bridge.reply(
      callback,
      G9Envelope.ok(mapOf("webSocket" to true, "binary" to true, "files" to true, "platform" to "android", "version" to VERSION)),
    )

  @LynxMethod
  override fun wsOpen(socketId: String, url: String, protocols: ReadableArray?, headers: ReadableMap?, callback: Callback?) =
    sockets.open(socketId, url, G9Bridge.stringList(protocols), G9Bridge.stringMap(headers)) { G9Bridge.reply(callback, it) }

  @LynxMethod
  override fun wsSendText(socketId: String, text: String, callback: Callback?) =
    sockets.sendText(socketId, text) { G9Bridge.reply(callback, it) }

  @LynxMethod
  override fun wsSendBinary(socketId: String, data: ByteArray?, callback: Callback?) {
    val bytes = data ?: return G9Bridge.reply(callback, G9Envelope.fail("invalid", "data must be an ArrayBuffer."))
    sockets.sendBinary(socketId, bytes) { G9Bridge.reply(callback, it) }
  }

  @LynxMethod
  override fun wsClose(socketId: String, code: Double, reason: String, callback: Callback?) =
    sockets.close(socketId, code.toInt(), reason) { G9Bridge.reply(callback, it) }

  @LynxMethod
  override fun wsPoll(socketId: String, callback: Callback?) = sockets.poll(socketId) { G9Bridge.reply(callback, it) }

  @LynxMethod
  override fun fileStat(path: String, callback: Callback?) = onFiles(callback) { G9FileCore.stat(path) }

  @LynxMethod
  override fun fileRead(path: String, offset: Double, length: Double, callback: Callback?) =
    onFiles(callback) { G9FileCore.read(path, offset.toLong(), length.toInt()) }

  @LynxMethod
  override fun fileWrite(path: String, offset: Double, data: ByteArray?, truncate: Boolean, callback: Callback?) {
    val bytes = data ?: return G9Bridge.reply(callback, G9Envelope.fail("invalid", "data must be an ArrayBuffer."))
    onFiles(callback) { G9FileCore.write(path, offset.toLong(), bytes, truncate) }
  }

  @LynxMethod
  override fun fileMove(from: String, to: String, callback: Callback?) = onFiles(callback) { G9FileCore.move(from, to) }

  @LynxMethod
  override fun fileDelete(path: String, callback: Callback?) = onFiles(callback) { G9FileCore.delete(path) }

  private fun onFiles(callback: Callback?, work: () -> Map<String, Any?>) {
    files.execute { G9Bridge.reply(callback, work()) }
  }
}
