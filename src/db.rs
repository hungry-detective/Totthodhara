//! SQLite persistence — schema byte-compatible with sqlite-net-pcl's
//! `CreateTable<ClipboardItem>` (DateAdded = .NET ticks, local time).
use rusqlite::{params, Connection, Row};

pub const WELCOME_TITLE: &str = "Welcome to Totthodhara";

#[derive(Clone, Debug, Default)]
pub struct Item {
    pub id: i64,
    pub file_name: String,
    pub file_path: String,
    pub text_content: String,
    pub is_file: bool,
    pub is_pinned: bool,
    pub is_snippet: bool,
    pub date_added: i64,
    pub display_title: String,
    pub icon_glyph: String,
    pub origin: String,
}

fn ext_of(p: &str) -> String {
    std::path::Path::new(p)
        .extension()
        .map(|e| format!(".{}", e.to_string_lossy().to_lowercase()))
        .unwrap_or_default()
}

impl Item {
    pub fn ext(&self) -> String {
        ext_of(&self.file_path)
    }
    pub fn is_image(&self) -> bool {
        self.is_file && matches!(self.ext().as_str(), ".png" | ".jpg" | ".jpeg" | ".gif" | ".bmp" | ".ico")
    }
    pub fn is_video(&self) -> bool {
        self.is_file && matches!(self.ext().as_str(), ".mp4" | ".avi" | ".mkv" | ".mov" | ".wmv")
    }
    pub fn is_pdf(&self) -> bool {
        self.is_file && self.ext() == ".pdf"
    }
    pub fn is_audio(&self) -> bool {
        self.is_file
            && matches!(self.ext().as_str(), ".mp3" | ".wav" | ".wma" | ".m4a" | ".ogg" | ".flac" | ".aac")
    }
    pub fn is_color(&self) -> bool {
        let t = &self.text_content;
        (t.len() == 7 || t.len() == 9)
            && t.starts_with('#')
            && t[1..].chars().all(|c| c.is_ascii_hexdigit())
            // WPF used int.TryParse → 8 hex digits must fit in i32
            && (t.len() == 7 || u32::from_str_radix(&t[1..], 16).map(|v| v <= i32::MAX as u32).unwrap_or(false))
    }
}

pub struct Db {
    conn: Connection,
}

const COLS: &str = "Id, FileName, FilePath, TextContent, IsFile, IsPinned, IsSnippet, DateAdded, DisplayTitle, IconGlyph, Origin";

fn row_to_item(r: &Row) -> rusqlite::Result<Item> {
    Ok(Item {
        id: r.get(0)?,
        file_name: r.get::<_, Option<String>>(1)?.unwrap_or_default(),
        file_path: r.get::<_, Option<String>>(2)?.unwrap_or_default(),
        text_content: r.get::<_, Option<String>>(3)?.unwrap_or_default(),
        is_file: r.get::<_, Option<i64>>(4)?.unwrap_or(0) != 0,
        is_pinned: r.get::<_, Option<i64>>(5)?.unwrap_or(0) != 0,
        is_snippet: r.get::<_, Option<i64>>(6)?.unwrap_or(0) != 0,
        date_added: r.get::<_, Option<i64>>(7)?.unwrap_or(0),
        display_title: r.get::<_, Option<String>>(8)?.unwrap_or_default(),
        icon_glyph: r.get::<_, Option<String>>(9)?.unwrap_or_default(),
        origin: r.get::<_, Option<String>>(10)?.unwrap_or_default(),
    })
}

impl Db {
    pub fn open() -> rusqlite::Result<Self> {
        let conn = Connection::open(crate::paths::data_dir().join("metadata.db"))?;
        conn.execute_batch(
            r#"CREATE TABLE IF NOT EXISTS "ClipboardItem"(
                "Id" integer primary key autoincrement not null,
                "FileName" varchar, "FilePath" varchar, "TextContent" varchar,
                "IsFile" integer, "IsPinned" integer, "IsSnippet" integer,
                "DateAdded" bigint, "DisplayTitle" varchar, "IconGlyph" varchar,
                "Origin" varchar);
               PRAGMA journal_mode=WAL;"#,
        )?;
        Ok(Self { conn })
    }

    pub fn items(&self, limit: usize) -> Vec<Item> {
        let sql = format!("SELECT {COLS} FROM ClipboardItem ORDER BY DateAdded DESC LIMIT ?1");
        let Ok(mut st) = self.conn.prepare(&sql) else { return vec![] };
        st.query_map([limit as i64], row_to_item)
            .map(|it| it.flatten().collect())
            .unwrap_or_default()
    }

    pub fn all_items(&self) -> Vec<Item> {
        let sql = format!("SELECT {COLS} FROM ClipboardItem");
        let Ok(mut st) = self.conn.prepare(&sql) else { return vec![] };
        st.query_map([], row_to_item).map(|it| it.flatten().collect()).unwrap_or_default()
    }

    /// The most recent item (used for "same as top item" dedupe).
    pub fn top(&self) -> Option<Item> {
        self.items(1).into_iter().next()
    }

    pub fn add(&self, it: &Item) -> i64 {
        let _ = self.conn.execute(
            "INSERT INTO ClipboardItem (FileName, FilePath, TextContent, IsFile, IsPinned, IsSnippet, DateAdded, DisplayTitle, IconGlyph, Origin) VALUES (?1,?2,?3,?4,?5,?6,?7,?8,?9,?10)",
            params![it.file_name, it.file_path, it.text_content, it.is_file as i64, it.is_pinned as i64, it.is_snippet as i64, it.date_added, it.display_title, it.icon_glyph, it.origin],
        );
        self.conn.last_insert_rowid()
    }

    pub fn set_flags(&self, id: i64, pinned: bool, snippet: bool) {
        let _ = self.conn.execute(
            "UPDATE ClipboardItem SET IsPinned=?1, IsSnippet=?2 WHERE Id=?3",
            params![pinned as i64, snippet as i64, id],
        );
    }

    pub fn update_text(&self, id: i64, text: &str) {
        let _ = self.conn.execute("UPDATE ClipboardItem SET TextContent=?1 WHERE Id=?2", params![text, id]);
    }

    pub fn delete(&self, id: i64) {
        let _ = self.conn.execute("DELETE FROM ClipboardItem WHERE Id=?1", [id]);
    }

    pub fn delete_all_except_pinned(&self) {
        let _ = self.conn.execute(
            "DELETE FROM ClipboardItem WHERE (IsPinned = 0 AND IsSnippet = 0) OR DisplayTitle = ?1",
            [WELCOME_TITLE],
        );
    }

    pub fn delete_welcome(&self) {
        let _ = self.conn.execute("DELETE FROM ClipboardItem WHERE DisplayTitle = ?1", [WELCOME_TITLE]);
    }
}
