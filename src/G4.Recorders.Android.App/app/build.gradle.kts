plugins {
    id("com.android.application")
}

android {
    namespace = "com.g4.recorders.android"
    compileSdk = 36

    defaultConfig {
        applicationId = "com.g4.recorders.android"
        minSdk = 26
        targetSdk = 35
        versionCode = 1
        versionName = "1.0.0"
    }

    buildTypes {
        release {
            // Gson reflects over the peek request model, so the release build is not minified.
            isMinifyEnabled = false

            // The recorder is a developer tool installed by adb, never through a store. It is signed with the
            // debug key so every build installs without a keystore secret; the host launcher reinstalls the app
            // when a device holds a build signed with another machine's debug key.
            signingConfig = signingConfigs.getByName("debug")
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    testOptions {
        // The core classes under test never touch Android APIs; default values keep stray calls from failing.
        unitTests.isReturnDefaultValues = true
    }
}

dependencies {
    implementation("androidx.appcompat:appcompat:1.8.0")
    implementation("androidx.preference:preference-ktx:1.2.1")
    implementation("com.microsoft.signalr:signalr:10.0.15")

    testImplementation("junit:junit:4.13.2")
}

// Copy the signed release APK to the sibling G4.Recorders.Android.App.Drop folder under a fixed name. The .NET host
// project links that folder and copies it to its output under the same name, as it does for the Chromium extension.
val exportRecorderApk by tasks.registering(Copy::class) {
    from(layout.buildDirectory.file("outputs/apk/release/app-release.apk"))
    into(rootProject.layout.projectDirectory.dir("../G4.Recorders.Android.App.Drop"))
    rename { "g4-recorder.apk" }
}

// Export the APK every time the release build is assembled.
afterEvaluate {
    tasks.named("assembleRelease") {
        finalizedBy(exportRecorderApk)
    }
}
