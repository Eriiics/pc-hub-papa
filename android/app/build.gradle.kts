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
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            // Para pruebas personales se firma con la clave de debug
            signingConfig = signingConfigs.getByName("debug")
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
