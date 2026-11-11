// Resolve the Android Gradle plugin from Google's repository first, then Maven Central for its own dependencies.
pluginManagement {
    repositories {
        google()
        mavenCentral()
        gradlePluginPortal()
    }
}

// Keep every dependency repository in one place so the module build files cannot add their own.
dependencyResolutionManagement {
    repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
    repositories {
        google()
        mavenCentral()
    }
}

rootProject.name = "G4.Recorders.Android.App"
include(":app")
