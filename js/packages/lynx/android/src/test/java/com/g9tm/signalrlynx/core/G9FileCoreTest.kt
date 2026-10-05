package com.g9tm.signalrlynx.core

import org.junit.After
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Before
import org.junit.Test
import java.io.File
import java.nio.file.Files
import java.util.concurrent.TimeUnit

/** The file half of the module contract (what the .NET-twin uploader and downloader need). */
class G9FileCoreTest {
  private lateinit var dir: File

  @Before
  fun create() {
    dir = Files.createTempDirectory("g9-files").toFile()
  }

  @After
  fun delete() {
    dir.deleteRecursively()
  }

  @Suppress("UNCHECKED_CAST")
  private fun value(reply: Map<String, Any?>): Map<String, Any?> {
    assertEquals(reply.toString(), true, reply["ok"])
    return (reply["value"] ?: emptyMap<String, Any?>()) as Map<String, Any?>
  }

  private fun code(reply: Map<String, Any?>): Any? = (reply["error"] as Map<*, *>)["code"]

  @Test
  fun statReportsTheDotNetFileInfoFacts() {
    val file = File(dir, "Photo.JPG")
    file.writeBytes(ByteArray(1234) { it.toByte() })
    val stat = value(G9FileCore.stat(file.path))
    assertEquals(true, stat["exists"])
    assertEquals(1234.0, stat["size"])
    assertEquals(file.absoluteFile.normalize().path, stat["fullPath"])
    val nanos = Files.getLastModifiedTime(file.toPath()).to(TimeUnit.NANOSECONDS)
    assertEquals((621355968000000000L + nanos / 100).toString(), stat["lastWriteTicks"])
    assertEquals(false, value(G9FileCore.stat(File(dir, "missing").path))["exists"])
  }

  @Test
  fun writesAtOffsetsCreatesDirectoriesTruncatesAndReadsRanges() {
    val path = File(dir, "a/b/out.partial").path
    value(G9FileCore.write(path, 0, byteArrayOf(1, 2, 3, 4, 5, 6), false))
    value(G9FileCore.write(path, 2, byteArrayOf(9, 9), true))
    assertArrayEquals(byteArrayOf(1, 2, 9, 9), File(path).readBytes())
    assertArrayEquals(byteArrayOf(2, 9), value(G9FileCore.read(path, 1, 2))["data"] as ByteArray)
    assertArrayEquals(byteArrayOf(9), value(G9FileCore.read(path, 3, 100))["data"] as ByteArray)
    assertEquals("not-found", code(G9FileCore.read(File(dir, "none").path, 0, 1)))
    assertEquals("invalid", code(G9FileCore.read(path, -1, 1)))
  }

  @Test
  fun movesOnlyOntoAFreeTargetAndDeletesIdempotently() {
    val from = File(dir, "x.partial").apply { writeText("x") }
    val taken = File(dir, "taken").apply { writeText("t") }
    assertEquals("exists", code(G9FileCore.move(from.path, taken.path)))
    value(G9FileCore.move(from.path, File(dir, "sub/x").path))
    assertEquals("x", File(dir, "sub/x").readText())
    assertEquals("not-found", code(G9FileCore.move(from.path, File(dir, "y").path)))
    value(G9FileCore.delete(taken.path))
    value(G9FileCore.delete(taken.path))
  }
}
