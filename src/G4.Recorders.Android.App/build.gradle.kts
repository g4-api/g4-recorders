// Declare the Android application plugin once at the root; the app module applies it.
// AGP 9 compiles Kotlin sources itself, so no separate Kotlin plugin is applied.
plugins {
    id("com.android.application") version "9.5.1" apply false
}
