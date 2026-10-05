package com.g9tm.signalrlynx.core

import okhttp3.Response
import okhttp3.WebSocket
import okhttp3.WebSocketListener
import okhttp3.mockwebserver.MockResponse
import okhttp3.mockwebserver.MockWebServer
import okio.ByteString
import org.junit.After
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import java.util.concurrent.CopyOnWriteArrayList
import java.util.concurrent.CountDownLatch
import java.util.concurrent.LinkedBlockingQueue
import java.util.concurrent.TimeUnit

/** The socket half of the module contract against a real WebSocket server (OkHttp's MockWebServer). */
class G9SocketCoreTest {
  private lateinit var server: MockWebServer
  private val core = G9SocketCore()
  private val serverSockets = CopyOnWriteArrayList<WebSocket>()
  private val received = LinkedBlockingQueue<Any>()

  /** Echoes every message back; records it. */
  private val echo =
    object : WebSocketListener() {
      override fun onOpen(webSocket: WebSocket, response: Response) {
        serverSockets.add(webSocket)
      }

      override fun onMessage(webSocket: WebSocket, text: String) {
        received.add(text)
        webSocket.send(text)
      }

      override fun onMessage(webSocket: WebSocket, bytes: ByteString) {
        received.add(bytes.toByteArray())
        webSocket.send(bytes)
      }

      override fun onClosing(webSocket: WebSocket, code: Int, reason: String) {
        webSocket.close(code, reason)
      }
    }

  @Before
  fun start() {
    server = MockWebServer()
    server.start()
  }

  @After
  fun stop() {
    server.shutdown()
  }

  private fun url(): String = server.url("/hub").toString().replace("http://", "ws://")

  private fun call(block: (G9Reply) -> Unit): Map<String, Any?> {
    val replies = LinkedBlockingQueue<Map<String, Any?>>()
    block { replies.add(it) }
    return replies.poll(10, TimeUnit.SECONDS) ?: error("no reply")
  }

  @Suppress("UNCHECKED_CAST")
  private fun events(id: String): List<Map<String, Any?>> {
    val reply = call { core.poll(id, it) }
    assertEquals(true, reply["ok"])
    return (reply["value"] as Map<String, Any?>)["events"] as List<Map<String, Any?>>
  }

  /** Polls until an event of `type` arrives; returns everything polled so far. */
  private fun until(id: String, type: String): List<Map<String, Any?>> {
    val all = ArrayList<Map<String, Any?>>()
    while (all.none { it["type"] == type }) all.addAll(events(id))
    return all
  }

  @Test
  fun opensWithProtocolAndHeadersThenEchoesTextAndBinaryInOrder() {
    server.enqueue(MockResponse().withWebSocketUpgrade(echo).setHeader("Sec-WebSocket-Protocol", "g9"))
    assertEquals(true, call { core.open("a", url(), listOf("g9", "other"), mapOf("X-G9" to "lynx"), it) }["ok"])

    val opened = until("a", "open")
    assertEquals("g9", opened.first { it["type"] == "open" }["protocol"])
    val request = server.takeRequest()
    assertEquals("lynx", request.getHeader("X-G9"))
    assertEquals("g9, other", request.getHeader("Sec-WebSocket-Protocol"))

    assertEquals(true, call { core.sendText("a", "one", it) }["ok"])
    assertEquals(true, call { core.sendBinary("a", byteArrayOf(1, 2, 3), it) }["ok"])
    assertEquals(true, call { core.sendText("a", "two", it) }["ok"])

    val messages = ArrayList<Map<String, Any?>>()
    while (messages.size < 3) messages.addAll(events("a").filter { it["type"] == "text" || it["type"] == "binary" })
    assertEquals(listOf("text", "binary", "text"), messages.map { it["type"] })
    assertEquals("one", messages[0]["data"])
    assertArrayEquals(byteArrayOf(1, 2, 3), messages[1]["data"] as ByteArray)
    assertEquals("two", messages[2]["data"])

    assertEquals(true, call { core.close("a", 1000, "bye", it) }["ok"])
    val closed = until("a", "close").last()
    assertEquals(1000, closed["code"])
    assertEquals(true, closed["wasClean"])
    assertEquals(0, core.openSockets)
  }

  @Test
  fun deliversAServerCloseWithItsCodeAndReasonOnce() {
    server.enqueue(MockResponse().withWebSocketUpgrade(echo))
    call { core.open("b", url(), emptyList(), emptyMap(), it) }
    until("b", "open")
    serverSockets.single().close(4001, "kicked")
    val close = until("b", "close").last()
    assertEquals(4001, close["code"])
    assertEquals("kicked", close["reason"])
    // The socket is gone: a further poll reports a synthetic close, and sends are refused.
    assertEquals("close", events("b").single()["type"])
    assertEquals(false, call { core.sendText("b", "late", it) }["ok"])
  }

  @Test
  fun reportsAFailedConnectAsErrorThenAbnormalClose() {
    server.enqueue(MockResponse().setResponseCode(404))
    call { core.open("c", url(), emptyList(), emptyMap(), it) }
    val all = until("c", "close")
    assertEquals(listOf("error", "close"), all.map { it["type"] })
    assertEquals(1006, all.last()["code"])
    assertEquals(false, all.last()["wasClean"])
  }

  @Test
  fun refusesASecondConcurrentPollAndDuplicateIds() {
    server.enqueue(MockResponse().withWebSocketUpgrade(echo))
    call { core.open("d", url(), emptyList(), emptyMap(), it) }
    until("d", "open")
    val first = LinkedBlockingQueue<Map<String, Any?>>()
    core.poll("d") { first.add(it) }
    val second = call { core.poll("d", it) }
    assertEquals(false, second["ok"])
    assertEquals("busy", (second["error"] as Map<*, *>)["code"])
    assertEquals("exists", ((call { core.open("d", url(), emptyList(), emptyMap(), it) })["error"] as Map<*, *>)["code"])
    core.sendText("d", "wake") {}
    assertTrue(first.poll(10, TimeUnit.SECONDS)!!["ok"] == true)
    core.close("d", 1000, "") {}
    until("d", "close")
  }

  @Test
  fun queuesEventsThatArriveBeforeAPollAndDeliversThemTogether() {
    val latch = CountDownLatch(1)
    server.enqueue(
      MockResponse().withWebSocketUpgrade(
        object : WebSocketListener() {
          override fun onOpen(webSocket: WebSocket, response: Response) {
            for (i in 1..50) webSocket.send("m$i")
            latch.countDown()
          }

          override fun onClosing(webSocket: WebSocket, code: Int, reason: String) {
            webSocket.close(code, reason)
          }
        },
      ),
    )
    call { core.open("e", url(), emptyList(), emptyMap(), it) }
    assertTrue(latch.await(10, TimeUnit.SECONDS))
    Thread.sleep(200)
    val batch = events("e")
    assertEquals("open", batch.first()["type"])
    val texts = ArrayList(batch.filter { it["type"] == "text" }.map { it["data"] })
    while (texts.size < 50) texts.addAll(events("e").filter { it["type"] == "text" }.map { it["data"] })
    assertEquals((1..50).map { "m$it" }, texts)
    assertTrue("events arrived batched", batch.size > 2)
    core.close("e", 1000, "") {}
    until("e", "close")
  }
}
