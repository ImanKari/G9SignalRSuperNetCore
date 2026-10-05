package com.g9tm.signalrlynx

import com.lynx.react.bridge.Callback
import com.lynx.react.bridge.JavaOnlyArray
import com.lynx.react.bridge.JavaOnlyMap
import com.lynx.react.bridge.ReadableArray
import com.lynx.react.bridge.ReadableMap

/**
 * Copies between Lynx bridge containers and the plain Kotlin data the cores speak (`Map`, `List`, `String`, `Number`,
 * `Boolean`, `ByteArray` ⇄ ArrayBuffer, `null`). Everything else of the module is plain JVM code.
 */
internal object G9Bridge {
  fun stringList(value: Any?): List<String> =
    when (value) {
      is ReadableArray -> (0 until value.size()).mapNotNull { value.getString(it) }
      is List<*> -> value.mapNotNull { it?.toString() }
      else -> emptyList()
    }

  fun stringMap(value: Any?): Map<String, String> {
    val map: Map<*, *> =
      when (value) {
        is ReadableMap -> value.toHashMap()
        is Map<*, *> -> value
        else -> emptyMap<String, Any?>()
      }
    val out = LinkedHashMap<String, String>()
    for ((k, v) in map) if (k is String && v != null) out[k] = v.toString()
    return out
  }

  fun toMap(value: Map<*, *>): JavaOnlyMap {
    val out = JavaOnlyMap()
    for ((k, v) in value) {
      val key = k as String
      when (v) {
        null -> out.putNull(key)
        is Boolean -> out.putBoolean(key, v)
        is Number -> out.putDouble(key, v.toDouble())
        is String -> out.putString(key, v)
        is ByteArray -> out.putByteArray(key, v)
        is Map<*, *> -> out.putMap(key, toMap(v))
        is List<*> -> out.putArray(key, toArray(v))
        else -> out.putString(key, v.toString())
      }
    }
    return out
  }

  fun toArray(value: List<*>): JavaOnlyArray {
    val out = JavaOnlyArray()
    for (v in value) {
      when (v) {
        null -> out.pushNull()
        is Boolean -> out.pushBoolean(v)
        is Number -> out.pushDouble(v.toDouble())
        is String -> out.pushString(v)
        is ByteArray -> out.pushByteArray(v)
        is Map<*, *> -> out.pushMap(toMap(v))
        is List<*> -> out.pushArray(toArray(v))
        else -> out.pushString(v.toString())
      }
    }
    return out
  }

  /** Invokes a JS callback with one envelope (Lynx marshals the call to the JS thread). */
  fun reply(callback: Callback?, envelope: Map<String, Any?>) {
    callback?.invoke(toMap(envelope))
  }
}
