package com.g9tm.signalrlynx.core

import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.Response
import okhttp3.WebSocket
import okhttp3.WebSocketListener
import okio.ByteString
import okio.ByteString.Companion.toByteString
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.TimeUnit

/**
 * The WebSocket half of the module contract over OkHttp, platform-free so it runs on the JVM (`npm run
 * test:android-jvm`). Each socket queues its events; [poll] replies with everything queued, or waits for the next
 * event. The last event of a socket is always `close` (after OkHttp's `onClosed` or `onFailure`), and the socket is
 * forgotten once that event has been delivered.
 */
class G9SocketCore(private val client: OkHttpClient = defaultClient()) {
  companion object {
    /** Read timeout 0: a WebSocket is idle between messages; SignalR's own keep-alive detects a dead peer. */
    fun defaultClient(): OkHttpClient =
      OkHttpClient.Builder()
        .connectTimeout(20, TimeUnit.SECONDS)
        .readTimeout(0, TimeUnit.MILLISECONDS)
        .writeTimeout(30, TimeUnit.SECONDS)
        .pingInterval(0, TimeUnit.MILLISECONDS)
        .build()
  }

  private class Socket(val id: String) {
    var webSocket: WebSocket? = null
    val queue = ArrayList<Map<String, Any?>>()
    var waiter: G9Reply? = null
    var closeQueued = false
  }

  private val sockets = ConcurrentHashMap<String, Socket>()

  /** Number of sockets not yet fully delivered (tests). */
  val openSockets: Int get() = sockets.size

  fun open(id: String, url: String, protocols: List<String>, headers: Map<String, String>, reply: G9Reply) {
    if (sockets.containsKey(id)) return reply(G9Envelope.fail("exists", "Socket $id is already open."))
    val request =
      try {
        val builder = Request.Builder().url(url)
        for ((name, value) in headers) builder.header(name, value)
        if (protocols.isNotEmpty()) builder.header("Sec-WebSocket-Protocol", protocols.joinToString(", "))
        builder.build()
      } catch (error: IllegalArgumentException) {
        return reply(G9Envelope.fail("socket", error.message))
      }
    val socket = Socket(id)
    sockets[id] = socket
    socket.webSocket = client.newWebSocket(request, Listener(socket))
    reply(G9Envelope.ok())
  }

  fun sendText(id: String, text: String, reply: G9Reply) {
    val webSocket = live(id) ?: return reply(G9Envelope.fail("closed", "Socket $id is not open."))
    reply(if (webSocket.send(text)) G9Envelope.ok() else G9Envelope.fail("socket", "The socket refused the message (closing, or 16 MiB queued)."))
  }

  fun sendBinary(id: String, data: ByteArray, reply: G9Reply) {
    val webSocket = live(id) ?: return reply(G9Envelope.fail("closed", "Socket $id is not open."))
    reply(if (webSocket.send(data.toByteString())) G9Envelope.ok() else G9Envelope.fail("socket", "The socket refused the message (closing, or 16 MiB queued)."))
  }

  fun close(id: String, code: Int, reason: String, reply: G9Reply) {
    val socket = sockets[id] ?: return reply(G9Envelope.ok())
    try {
      socket.webSocket?.close(if (code == 0) 1000 else code, reason.ifEmpty { null })
      reply(G9Envelope.ok())
    } catch (error: IllegalArgumentException) {
      reply(G9Envelope.fail("socket", error.message))
    }
  }

  fun poll(id: String, reply: G9Reply) {
    val socket = sockets[id]
      ?: return reply(G9Envelope.ok(mapOf("events" to listOf(closeEvent(1006, "Unknown socket.", false)))))
    val events: List<Map<String, Any?>>
    synchronized(socket) {
      if (socket.waiter != null) return reply(G9Envelope.fail("busy", "Only one wsPoll at a time per socket."))
      if (socket.queue.isEmpty()) {
        socket.waiter = reply
        return
      }
      events = ArrayList(socket.queue)
      socket.queue.clear()
    }
    deliver(id, events, reply)
  }

  private fun live(id: String): WebSocket? {
    val socket = sockets[id] ?: return null
    synchronized(socket) { if (socket.closeQueued) return null }
    return socket.webSocket
  }

  private fun deliver(id: String, events: List<Map<String, Any?>>, reply: G9Reply) {
    if (events.any { it["type"] == "close" }) sockets.remove(id)
    reply(G9Envelope.ok(mapOf("events" to events)))
  }

  private fun closeEvent(code: Int, reason: String, wasClean: Boolean): Map<String, Any?> =
    mapOf("type" to "close", "code" to code, "reason" to reason, "wasClean" to wasClean)

  private inner class Listener(private val socket: Socket) : WebSocketListener() {
    private fun push(event: Map<String, Any?>) {
      val waiter: G9Reply
      val events: List<Map<String, Any?>>
      synchronized(socket) {
        if (socket.closeQueued) return
        if (event["type"] == "close") socket.closeQueued = true
        socket.queue.add(event)
        waiter = socket.waiter ?: return
        socket.waiter = null
        events = ArrayList(socket.queue)
        socket.queue.clear()
      }
      deliver(socket.id, events, waiter)
    }

    override fun onOpen(webSocket: WebSocket, response: Response) {
      push(mapOf("type" to "open", "protocol" to (response.header("Sec-WebSocket-Protocol") ?: "")))
    }

    override fun onMessage(webSocket: WebSocket, text: String) {
      push(mapOf("type" to "text", "data" to text))
    }

    override fun onMessage(webSocket: WebSocket, bytes: ByteString) {
      push(mapOf("type" to "binary", "data" to bytes.toByteArray()))
    }

    override fun onClosing(webSocket: WebSocket, code: Int, reason: String) {
      // Complete the closing handshake the peer started; onClosed follows.
      webSocket.close(code, null)
    }

    override fun onClosed(webSocket: WebSocket, code: Int, reason: String) {
      push(closeEvent(code, reason, true))
    }

    override fun onFailure(webSocket: WebSocket, t: Throwable, response: Response?) {
      val message = t.message ?: t.javaClass.simpleName
      push(mapOf("type" to "error", "message" to message))
      push(closeEvent(1006, message, false))
    }
  }
}
