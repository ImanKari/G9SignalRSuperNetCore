// JVM-only build of the Android core (../android/src/main/java/com/g9tm/signalrlynx/core): the socket and file halves
// of the module contract run on the host JVM, without the Android SDK. `npm run test:android-jvm` runs it.
pluginManagement {
  repositories {
    gradlePluginPortal()
    mavenCentral()
  }
}

dependencyResolutionManagement { repositories { mavenCentral() } }

rootProject.name = "g9-signalr-lynx-jvm"
