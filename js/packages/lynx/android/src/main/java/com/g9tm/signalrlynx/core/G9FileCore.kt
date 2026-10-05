package com.g9tm.signalrlynx.core

import java.io.File
import java.io.FileNotFoundException
import java.io.IOException
import java.io.RandomAccessFile
import java.nio.file.Files
import java.util.concurrent.TimeUnit

/**
 * The file half of the module contract: what `G9FileUploader.uploadFile` and `G9FileDownloader.downloadToFile` need
 * (the .NET twins' `FileInfo` facts, ranged reads, positioned writes for the `.partial` file, rename, delete).
 * Platform-free (java.io + java.nio.file, API 26+), tested on the JVM.
 */
object G9FileCore {
  /** .NET ticks (100 ns since 0001-01-01) at the Unix epoch. */
  private const val UNIX_EPOCH_TICKS = 621355968000000000L

  fun stat(path: String): Map<String, Any?> {
    return try {
      val file = File(path)
      if (!file.isFile) return G9Envelope.ok(mapOf("exists" to false))
      val nanos =
        try {
          Files.getLastModifiedTime(file.toPath()).to(TimeUnit.NANOSECONDS)
        } catch (error: Exception) {
          file.lastModified() * 1_000_000L
        }
      G9Envelope.ok(
        mapOf(
          "exists" to true,
          "size" to file.length().toDouble(),
          "fullPath" to file.absoluteFile.normalize().path,
          "lastWriteTicks" to (UNIX_EPOCH_TICKS + nanos / 100L).toString(),
        ),
      )
    } catch (error: Exception) {
      failure(error)
    }
  }

  fun read(path: String, offset: Long, length: Int): Map<String, Any?> {
    if (offset < 0 || length < 0) return G9Envelope.fail("invalid", "offset and length must be non-negative.")
    return try {
      RandomAccessFile(path, "r").use { file ->
        val buffer = ByteArray(length)
        file.seek(offset)
        var filled = 0
        while (filled < length) {
          val read = file.read(buffer, filled, length - filled)
          if (read <= 0) break
          filled += read
        }
        G9Envelope.ok(mapOf("data" to if (filled == length) buffer else buffer.copyOf(filled)))
      }
    } catch (error: Exception) {
      failure(error)
    }
  }

  fun write(path: String, offset: Long, data: ByteArray, truncate: Boolean): Map<String, Any?> {
    if (offset < 0) return G9Envelope.fail("invalid", "offset must be non-negative.")
    return try {
      val file = File(path)
      file.absoluteFile.parentFile?.mkdirs()
      RandomAccessFile(file, "rw").use { out ->
        out.seek(offset)
        out.write(data)
        if (truncate) out.setLength(offset + data.size)
        out.fd.sync()
      }
      G9Envelope.ok()
    } catch (error: Exception) {
      failure(error)
    }
  }

  fun move(from: String, to: String): Map<String, Any?> {
    return try {
      val source = File(from)
      val target = File(to)
      if (!source.exists()) return G9Envelope.fail("not-found", "No such file: $from")
      if (target.exists()) return G9Envelope.fail("exists", "The target file already exists: $to")
      target.absoluteFile.parentFile?.mkdirs()
      if (!source.renameTo(target)) Files.move(source.toPath(), target.toPath())
      G9Envelope.ok()
    } catch (error: Exception) {
      failure(error)
    }
  }

  fun delete(path: String): Map<String, Any?> {
    return try {
      val file = File(path)
      if (file.exists() && !file.delete()) throw IOException("Could not delete $path")
      G9Envelope.ok()
    } catch (error: Exception) {
      failure(error)
    }
  }

  private fun failure(error: Exception): Map<String, Any?> =
    when (error) {
      is FileNotFoundException, is java.nio.file.NoSuchFileException -> G9Envelope.fail("not-found", error.message)
      is java.nio.file.FileAlreadyExistsException -> G9Envelope.fail("exists", error.message)
      else -> G9Envelope.fail("io", error.message ?: error.javaClass.simpleName)
    }
}
