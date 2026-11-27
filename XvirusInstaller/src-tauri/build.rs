use std::{env, fs, path::PathBuf};

fn main() {
    // scripts/build-resources.mjs zips resources/ into src-tauri/payload.zip. It is embedded into
    // the installer binary with include_bytes! (see src/main.rs). An empty payload is embedded
    // when the zip is missing so `tauri dev` still works without staged resources.
    let manifest_dir = PathBuf::from(env::var("CARGO_MANIFEST_DIR").unwrap());
    let payload = manifest_dir.join("payload.zip");
    let out = PathBuf::from(env::var("OUT_DIR").unwrap()).join("payload.zip");

    println!("cargo:rerun-if-changed={}", payload.display());
    if payload.exists() {
        fs::copy(&payload, &out).expect("failed to copy payload.zip");
    } else {
        println!("cargo:warning=payload.zip not found; building installer without resources");
        fs::write(&out, []).expect("failed to write empty payload");
    }

    tauri_build::build()
}
