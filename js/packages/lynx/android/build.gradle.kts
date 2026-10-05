// @g9tm/signalr-supernetcore-lynx for Android. Linked into a host by Lynx Autolink from node_modules; the host's
// settings supply the plugin versions (AGP, Kotlin), like G9LynxControls' @g9lynx/native.
plugins {
  id("com.android.library")
  id("org.jetbrains.kotlin.android")
  id("org.jetbrains.kotlin.kapt")
}

android {
  namespace = "com.g9tm.signalrlynx"
  compileSdk = 36

  defaultConfig {
    minSdk = 26
    consumerProguardFiles("consumer-rules.pro")
  }

  compileOptions {
    sourceCompatibility = JavaVersion.VERSION_17
    targetCompatibility = JavaVersion.VERSION_17
  }

  testOptions { unitTests.isReturnDefaultValues = true }
}

kotlin { jvmToolchain(17) }

val lynx = "4.1.0"

dependencies {
  compileOnly("org.lynxsdk.lynx:lynx:$lynx")
  compileOnly("org.lynxsdk.lynx:service-api:$lynx")
  kapt("org.lynxsdk.lynx:lynx-processor:$lynx")
  // The generated spec imports androidx.annotation and lynx-processor's output uses @Keep, but Lynx's POMs declare it
  // at runtime scope only: without this line kapt fails in the host ("cannot access Keep").
  compileOnly("androidx.annotation:annotation:1.9.1")
  // The WebSocket. 4.12 is the floor; an app on OkHttp 5 resolves to it (same WebSocket API).
  implementation("com.squareup.okhttp3:okhttp:4.12.0")

  // The JVM suite (../jvm) also runs as Android local unit tests.
  testImplementation("junit:junit:4.13.2")
  testImplementation("com.squareup.okhttp3:mockwebserver:4.12.0")
}
