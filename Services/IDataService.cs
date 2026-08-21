using System.Collections.Generic;
using System.Threading.Tasks;
using ClipDropPro.Models;

namespace ClipDropPro.Services
{
    public interface IDataService
    {
        Task InitializeAsync();
        Task<List<ClipboardItem>> GetItemsAsync(int limit = 30);
        Task<List<ClipboardItem>> GetAllItemsAsync();
        Task<int> GetItemsCountAsync();
        Task AddItemAsync(ClipboardItem item);
        Task UpdateItemAsync(ClipboardItem item);
        Task DeleteItemAsync(ClipboardItem item);
        Task DeleteAllExceptPinnedAsync();
        Task TrimOldestUnpinnedAsync(int maxItems);
    }
}
