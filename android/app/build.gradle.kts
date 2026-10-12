plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.compose")
}

// Versión única para la app y el agente: archivo VERSION en la raíz del repositorio (x.y.z)
val appVersion: String = rootProject.file("../VERSION").takeIf { it.exists() }?.readText()?.trim()?.ifEmpty { null } ?: "0.0.0"
val appVersionCode: Int = appVersion.split('.').map { it.toIntOrNull() ?: 0 }.let { p ->
    (p.getOrElse(0) { 0 } * 10_000) + (p.getOrElse(1) { 0 } * 100) + p.getOrElse(2) { 0 }
}

android {
    namespace = "com.chacho.pchub"
    compileSdk = 35

    defaultConfig {
        applicationId = "com.chacho.pchub"
        minSdk = 26
        targetSdk = 35
        versionCode = appVersionCode
        versionName = appVersion
        manifestPlaceholders["appLabel"] = "PC Hub"
    }

    // Firma estable: la misma clave (android/pchub.keystore, va en el repositorio) en cualquier PC,
    // para que la app nueva se instale encima de la anterior sin desinstalar.
    signingConfigs {
        create("pchub") {
            storeFile = file("../pchub.keystore")
            storePassword = "android"
            keyAlias = "androiddebugkey"
            keyPassword = "android"
        }
    }

    buildTypes {
        // "PC Hub Prueba": otra app (com.chacho.pchub.prueba) que convive con la normal y no la reemplaza
        create("prueba") {
            initWith(getByName("release"))
            applicationIdSuffix = ".prueba"
            manifestPlaceholders["appLabel"] = "PC Hub Prueba"
            signingConfig = signingConfigs.getByName("pchub")
            matchingFallbacks += "release"
        }
        release {
            isMinifyEnabled = false
            signingConfig = signingConfigs.getByName("pchub")
        }
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions {
        jvmTarget = "17"
    }
    buildFeatures {
        compose = true
    }
}

dependencies {
    implementation(platform("androidx.compose:compose-bom:2024.12.01"))
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.foundation:foundation")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.compose.material:material-icons-extended")
    implementation("androidx.activity:activity-compose:1.9.3")
    implementation("androidx.core:core-ktx:1.15.0")
    implementation("androidx.lifecycle:lifecycle-runtime-compose:2.8.7")
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.9.0")
    implementation("com.squareup.okhttp3:okhttp:4.12.0")
    debugImplementation("androidx.compose.ui:ui-tooling")
}
