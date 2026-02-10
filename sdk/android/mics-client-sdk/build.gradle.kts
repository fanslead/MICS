plugins {
    id("org.jetbrains.kotlin.jvm") version "2.0.21" apply false
    id("com.google.protobuf") version "0.9.4" apply false
}

allprojects {
    repositories {
        mavenCentral()
    }
}

