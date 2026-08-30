using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ClipDropPro.Models;
using SQLite;

namespace ClipDropPro.Services
{
    public class SqliteDataService : IDataService
    {
        private SQLiteAsyncConnection? _db;
        private readonly string _dbPath;

        public SqliteDataService()
        {
            var dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            if (!Directory.Exists(dataDir))
            {
                Directory.CreateDirectory(dataDir);
            }
            _dbPath = Path.Combine(dataDir, "metadata.db");
        }

        public async Task InitializeAsync()
        {
            if (_db != null) return;
            _db = new SQLiteAsyncConnection(_dbPath);
            await _db.CreateTableAsync<ClipboardItem>();
        }

        public async Task<List<ClipboardItem>> GetItemsAsync(int limit = 30)
        {
            await InitializeAsync();
            return await _db!.Table<ClipboardItem>().OrderByDescending(x => x.DateAdded).Take(limit).ToListAsync();
        }

        public async Task<List<ClipboardItem>> GetAllItemsAsync()
        {
            await InitializeAsync();
            return await _db!.Table<ClipboardItem>().OrderByDescending(x => x.DateAdded).ToListAsync();
        }

        public async Task<int> GetItemsCountAsync()
        {
            await InitializeAsync();
            return await _db!.Table<ClipboardItem>().CountAsync();
        }

        public async Task AddItemAsync(ClipboardItem item)
        {
            await InitializeAsync();
            await _db!.InsertAsync(item);
        }

        public async Task UpdateItemAsync(ClipboardItem item)
        {
            await InitializeAsync();
            await _db!.UpdateAsync(item);
        }

        public async Task DeleteItemAsync(ClipboardItem item)
        {
            await InitializeAsync();
            await _db!.DeleteAsync(item);
        }

        public async Task DeleteAllExceptPinnedAsync()
        {
            await InitializeAsync();
            var itemsToDelete = await _db!.Table<ClipboardItem>()
                .Where(x => (!x.IsPinned && !x.IsSnippet) || x.DisplayTitle == "Welcome to Totthodhara")
                .ToListAsync();
            foreach (var item in itemsToDelete)
            {
                await _db.DeleteAsync(item);
            }
        }

        public async Task DeleteWelcomeItemsAsync()
        {
            await InitializeAsync();
            var itemsToDelete = await _db!.Table<ClipboardItem>()
                .Where(x => x.DisplayTitle == "Welcome to Totthodhara")
                .ToListAsync();
            foreach (var item in itemsToDelete)
            {
                await _db.DeleteAsync(item);
            }
        }

        public async Task TrimOldestUnpinnedAsync(int maxItems)
        {
            await InitializeAsync();
            var allItems = await _db!.Table<ClipboardItem>().OrderByDescending(x => x.DateAdded).ToListAsync();
            var toDelete = allItems.Where(x => !x.IsPinned && !x.IsSnippet).Skip(maxItems).ToList();
            foreach (var item in toDelete)
            {
                await _db.DeleteAsync(item);
            }
        }
    }
}
