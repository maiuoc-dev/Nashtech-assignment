using Ecommerce.SharedViewModel.DTOs;
using Ecommerce.SharedViewModel.Models;
using Microsoft.EntityFrameworkCore;
using Ecommerce.BackendAPI.Data;
using Ecommerce.BackendAPI.Interfaces;
using Ecommerce.SharedViewModel.Responses;
using Microsoft.EntityFrameworkCore.Storage;


namespace Ecommerce.BackendAPI.Repositories
{
    public class ProductRepository : IProductRepository
    {
        private readonly DataContext _context;

        public ProductRepository(DataContext context)
        {
            _context = context;
        }

        public void AttachProductClassification(ProductClassification productClassification)
        {
            _context.ProductClassifications.Attach(productClassification);
        }

        public async Task<IDbContextTransaction> BeginTransactionAsync()
        {
            return await _context.Database.BeginTransactionAsync();
        }

        public async Task<Product?> GetProductByIdAsync(int id, bool includeRelated = true)
        {
            if (!includeRelated)
            {
                return await _context.Products.FindAsync(id);
            }

            return await _context.Products
                .Include(p => p.Reviews)
                .ThenInclude(r => r.Customer)
                .Include(p => p.Variants)
                .ThenInclude(v => v.VariantCategories)
                .ThenInclude(vc => vc.Category)
                .ThenInclude(c => c.ParentCategory) // Include ParentCategory
                .Include(p => p.ProductClassifications)
                .ThenInclude(pc => pc.Classification)
                .Select(p => new Product
                {
                    Id = p.Id,
                    Name = p.Name,
                    Price = p.Price,
                    Description = p.Description,
                    ImageUrl = p.ImageUrl,
                    CreatedAt = p.CreatedAt,
                    UpdatedAt = p.UpdatedAt,
                    Variants = p.Variants,
                    ProductClassifications = p.ProductClassifications,
                    Reviews = p.Reviews
                        .OrderByDescending(r => r.CreatedAt) // Sort Reviews by CreatedAt in descending order
                        .ToList(),
                })
                .FirstOrDefaultAsync(p => p.Id == id);
        }

        public async Task<(int TotalProducts, IEnumerable<ProductsGetAllProductsResponse> Products)> GetAllProductsAsync
        (
            int pageNumber,
            int pageSize,
            string sortBy,
            bool isAsc,
            int? classificationId,
            int minPrice,
            int maxPrice,
            double minRating,
            double maxRating,
            string startDate,
            string endDate,
            string? search
        )
        {
            var query = _context.Products
                .Include(p => p.Reviews)
                .Include(p => p.Variants)
                .Include(p => p.ProductClassifications)
                .ThenInclude(pc => pc.Classification)
                .AsQueryable();

            // Filter by classification ID if provided
            if (classificationId.HasValue)
            {
                query = query.Where(p => p.ProductClassifications.Any(pc => pc.ClassificationId == classificationId.Value));
            }

            // Filter by price range
            query = query.Where(p => p.Price >= minPrice && p.Price <= maxPrice);

            // Filter by search term if provided
            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(p => p.Name.Contains(search) || p.Description.Contains(search));
            }

            // Filter by date range if provided
            if (!string.IsNullOrEmpty(startDate) && DateTime.TryParse(startDate, out var parsedStartDate))
            {
                query = query.Where(p => p.CreatedAt >= parsedStartDate);
            }
            
            if (!string.IsNullOrEmpty(endDate) && DateTime.TryParse(endDate, out var parsedEndDate))
            {
                // Add one day to include the entire endDate
                parsedEndDate = parsedEndDate.AddDays(1);
                query = query.Where(p => p.CreatedAt < parsedEndDate);
            }

            // Get the total number of products before applying pagination
            int totalProducts = await query.CountAsync();

            // Apply sorting
            query = isAsc
                ? query.OrderBy(p => EF.Property<object>(p, sortBy))
                : query.OrderByDescending(p => EF.Property<object>(p, sortBy));

            // Apply pagination
            query = query.Skip((pageNumber - 1) * pageSize).Take(pageSize);

            // Fetch the paginated list of products with additional details
            var productsWithRatings = await query
                .Select(p => new ProductsGetAllProductsResponse
                {
                    Id = p.Id,
                    Name = p.Name,
                    Price = p.Price,
                    Description = p.Description,
                    ImageUrl = p.ImageUrl,
                    CreatedAt = p.CreatedAt,
                    UpdatedAt = p.UpdatedAt,
                    AverageRating = p.Reviews.Any() ? p.Reviews.Average(r => r.Rating) : 0,
                    TotalOrders = p.Variants.SelectMany(vo => vo.VariantOrders).Count(),
                    TotalReviews = p.Reviews.Count(),
                    ClassificationNames = p.ProductClassifications
                        .Select(pc => pc.Classification.Name)
                        .ToList()
                })
                .ToListAsync();

            // Return both total products and the paginated list
            return (totalProducts, productsWithRatings);
        }
        
        public async Task<Product> CreateProductAsync(Product product, IList<Classification> classificationList)
        {
            var response = await _context.Products.AddAsync(product);
            foreach (var classification in classificationList)
            {
                var newItems = new ProductClassification
                {
                    Product = response.Entity,
                    Classification = classification
                };
                await _context.ProductClassifications.AddAsync(newItems);
            }
            await SaveAsync();
            return response.Entity;
        }

        public async Task<bool> UpdateProductAsync(Product product)
        {
            _context.Products.Update(product);
            return await SaveAsync();
        }
        
        public async Task<bool> DeleteProductAsync(int id)
        {
            var product = await GetProductByIdAsync(id);
            if (product == null) return false;

            _context.Products.Remove(product);
            return await SaveAsync();
        }

        public async Task<bool> SaveAsync()
        {
            return await _context.SaveChangesAsync() > 0;
        }
    }
}