fn main() {
    // Embed app.ico as the EXE icon (same as ApplicationIcon in the WPF csproj).
    embed_resource::compile("assets/app.rc", embed_resource::NONE)
        .manifest_optional()
        .ok();
}
