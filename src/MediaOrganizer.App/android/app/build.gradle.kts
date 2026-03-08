plugins {
    id("com.android.application")
    id("kotlin-android")
    // The Flutter Gradle Plugin must be applied after the Android and Kotlin Gradle plugins.
    id("dev.flutter.flutter-gradle-plugin")
}

android {
    namespace = "com.bramve.mediaorganizer"
    compileSdk = flutter.compileSdkVersion
    ndkVersion = flutter.ndkVersion

    val keystorePath: String? = System.getenv("ANDROID_KEYSTORE_PATH")
    val keystorePassword: String? = System.getenv("ANDROID_KEYSTORE_PASSWORD")
    val keystoreKeyAlias: String? = System.getenv("ANDROID_KEY_ALIAS")
    val keystoreKeyPassword: String? = System.getenv("ANDROID_KEY_PASSWORD")

    val useReleaseSigning =
        !keystorePath.isNullOrBlank() &&
            !keystorePassword.isNullOrBlank() &&
            !keystoreKeyAlias.isNullOrBlank() &&
            !keystoreKeyPassword.isNullOrBlank() &&
            (keystorePath?.let { file(it).exists() } ?: false)

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    // NOTE: Using Kotlin compilerOptions DSL instead of deprecated kotlinOptions.jvmTarget.
    defaultConfig {
        // TODO: Specify your own unique Application ID (https://developer.android.com/studio/build/application-id.html).
        applicationId = "com.bramve.mediaorganizer"
        // You can update the following values to match your application needs.
        // For more information, see: https://flutter.dev/to/review-gradle-config.
        minSdk = flutter.minSdkVersion
        targetSdk = flutter.targetSdkVersion
        versionCode = flutter.versionCode
        versionName = flutter.versionName
    }

    signingConfigs {
        if (useReleaseSigning) {
            create("release") {
                storeFile = file(keystorePath!!)
                storePassword = keystorePassword
                keyAlias = keystoreKeyAlias
                keyPassword = keystoreKeyPassword
            }
        }
    }

    buildTypes {
        release {
            signingConfig =
                if (useReleaseSigning) {
                    signingConfigs.getByName("release")
                } else {
                    // Signing with the debug keys when no release signing is configured.
                    signingConfigs.getByName("debug")
                }
        }
    }
}

kotlin {
    compilerOptions {
        jvmTarget.set(org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17)
    }
}

flutter {
    source = "../.."
}
