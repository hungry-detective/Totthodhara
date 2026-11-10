//! Embeds assets/app.rc (exe icon + manifest).
//!
//! GNU toolchain: run windres OURSELVES from inside assets/ with relative
//! inputs. embed-resource passes `-I <dir>` flags, and windres' preprocessor
//! breaks on paths containing spaces ("My Project") — that silently shipped
//! an exe with no icon once. Any failure here now fails the build.
fn main() {
    println!("cargo:rerun-if-changed=assets/app.rc");
    println!("cargo:rerun-if-changed=assets/app.ico");
    println!("cargo:rerun-if-changed=assets/app.manifest");
    let target_env = std::env::var("CARGO_CFG_TARGET_ENV").unwrap_or_default();
    if target_env == "msvc" {
        embed_resource::compile("assets/app.rc", embed_resource::NONE)
            .manifest_required()
            .expect("embedding app.rc failed");
        return;
    }
    let out = std::path::PathBuf::from(std::env::var("OUT_DIR").unwrap()).join("app_res.o");
    let assets = std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join("assets");
    let status = std::process::Command::new("windres")
        .current_dir(&assets)
        .args(["app.rc", "-O", "coff", "-o"])
        .arg(&out)
        .status()
        .expect("windres not found (needs MinGW on PATH)");
    assert!(status.success(), "windres failed to compile assets/app.rc");
    println!("cargo:rustc-link-arg-bins={}", out.display());
}
