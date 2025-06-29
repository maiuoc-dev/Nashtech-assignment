using Ecommerce.SharedViewModel.Models;
using Ecommerce.BackendAPI.Interfaces;
using Ecommerce.BackendAPI.FiltersAction;
using Microsoft.AspNetCore.Mvc;
using Ecommerce.SharedViewModel.ParametersType;
using Ecommerce.SharedViewModel.Responses;


namespace Ecommerce.BackendAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class OrderController : ControllerBase
    {
        private readonly IOrderRepository _orderRepository;
        private readonly IVariantRepository _variantRepository;
        private readonly ICartRepository _cartRepository;

        public OrderController
        (
            IOrderRepository orderRepository,
            IVariantRepository variantRepository,
            ICartRepository cartRepository
        )
        {
            _orderRepository = orderRepository;
            _variantRepository = variantRepository;
            _cartRepository = cartRepository;
        }

        [HttpGet()]
        [ServiceFilter(typeof(VerifyToken))]
        [ServiceFilter(typeof(GetAllOrdersFilter))]
        public async Task<IActionResult> GetAllOrders([FromQuery] int pageNumber = 1)
        {
            try
            {
                // Retrieve total orders and paginated list of orders
                var (totalOrders, orders) = await _orderRepository.GetAllOrdersAsync(pageNumber);

                // Format the response
                var response = new
                {
                    TotalOrders = totalOrders,
                    Orders = orders.Select(order => new
                    {
                        order.Id,
                        order.CreatedAt,
                        order.Amount,
                        order.Address,
                        Customer = order.Customer.Name
                    })
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Message = "An error occurred while retrieving orders.", Error = ex.Message });
            }
        }

        [HttpGet("{orderId}")]
        [ServiceFilter(typeof(VerifyToken))]
        [ServiceFilter(typeof(GetOrderFilter))]
        public async Task<IActionResult> GetOrderById(int orderId)
        {
            var order = await _orderRepository.GetOrderByIdAsync(orderId);
            if (order == null) return NotFound();
            
            var variantsResponse = order.VariantOrders
                .Select(vo => new VariantInGetOrderById
                {
                    Id = vo.Variant.Id,
                    SKU = vo.Variant.SKU,
                    Price = vo.Variant.Price,
                    ImageUrl = vo.Variant.ImageUrl,
                    Quantity = vo.Quantity,
                    Color = vo.Variant.VariantCategories
                        .Where(vc => vc.Category.ParentCategory != null && vc.Category.ParentCategory.Name.ToLower().Contains("color"))
                        .Select(vc => vc.Category.Name)
                        .FirstOrDefault(),
                    Size = vo.Variant.VariantCategories
                        .Where(vc => vc.Category.ParentCategory != null && vc.Category.ParentCategory.Name.ToLower().Contains("size"))
                        .Select(vc => vc.Category.Name)
                        .FirstOrDefault(),
                    Product = new ProductInGetOrderById
                    {
                        Id = vo.Variant.Product.Id,
                        Name = vo.Variant.Product.Name,
                        Description = vo.Variant.Product.Description,
                        ImageUrl = vo.Variant.Product.ImageUrl,
                        Price = vo.Variant.Product.Price,
                    }
                })
                .ToList();

            order.VariantOrders = null;

            return Ok(new GetOrderByIdResponse
            {
                Id = order.Id,
                Amount = order.Amount,
                Status = order.Status,
                CustomerName = order.CustomerName,
                Email = order.Email,
                PhoneNumber = order.PhoneNumber,
                Address = order.Address,
                CreatedAt = order.CreatedAt,
                Customer = new CustomerInOrderResponse
                {
                    Id = order.Customer.Id,
                    Name = order.Customer.Name,
                    Email = order.Customer.Email,
                    PhoneNumber = order.Customer.PhoneNumber,
                    Address = order.Customer.Address
                },
                Variants = variantsResponse
            });
        }

        [HttpGet("customer/{customerId}")]
        [ServiceFilter(typeof(VerifyToken))]
        [ServiceFilter(typeof(GetOrderFilter))]
        public async Task<IActionResult> GetOrdersByUserId(int customerId)
        {
            var orders = await _orderRepository.GetOrderByUserIdAsync(customerId);
            if (orders == null) return NotFound();

            var response = orders.Select(order => new
            {
                order.Id,
                order.CreatedAt,
                order.Amount,
                Variants = order.VariantOrders.Select(vo => new
                {
                    vo.Variant.Id,
                    vo.Quantity,
                    vo.Variant.Price,
                    vo.Variant.ImageUrl,
                    Color = vo.Variant.VariantCategories
                        .Where(vc => vc.Category.ParentCategory != null && vc.Category.ParentCategory.Name.ToLower().Contains("color"))
                        .Select(vc => vc.Category.Name)
                        .FirstOrDefault(),
                    Size = vo.Variant.VariantCategories
                        .Where(vc => vc.Category.ParentCategory != null && vc.Category.ParentCategory.Name.ToLower().Contains("size"))
                        .Select(vc => vc.Category.Name)
                        .FirstOrDefault(),
                    Product = new
                    {
                        vo.Variant.Product.Id,
                        vo.Variant.Product.Name,
                        vo.Variant.Product.ImageUrl,
                        vo.Variant.Product.Price
                    }
                }).ToList()
            });

            return Ok(response);
        }

        [HttpGet("product/{productId}")]
        [ServiceFilter(typeof(VerifyToken))]
        [ServiceFilter(typeof(GetOrderFilter))]
        public async Task<IActionResult> GetOrdersOfProduct(int productId)
        {
            var orders = await _orderRepository.GetOrdersOfProductAsync(productId);

            return Ok(orders);
        }

        [HttpPost]
        [ServiceFilter(typeof(VerifyToken))]
        [ServiceFilter(typeof(CreateOrderFilter))]
        public async Task<IActionResult> CreateOrder([FromBody] CreateOrderParameter request)
        {
            if (request == null) return BadRequest("Order cannot be null");

            var customer = HttpContext.Items["Customer"] as Customer;

            if (customer == null) {
                return BadRequest(new { Error = "Invalid customer" });
            }

            var variantOrders = new List<VariantOrder>();
            foreach(var item in request.Variants)
            {
                var variant = await _variantRepository.GetVariantByIdAsync(item.Id);
                if (variant == null) {
                    return BadRequest(new { message = $"Variant with Id={item.Id} not found" });
                }
                variantOrders.Add(new VariantOrder
                {
                    Variant = variant,
                    Quantity = item.Quantity
                });
            }

            var order = new Order {
                Customer = customer,
                Amount = request.Amount,
                CustomerName = request.CustomerName,
                Email = request.Email,
                PhoneNumber = request.PhoneNumber,
                Address = request.Address,
                VariantOrders = variantOrders
            };
            var result = await _orderRepository.CreateOrderAsync(order);

            // Clear the cart after order creation
            var cart = await _cartRepository.GetCartOfCustomerAsync(customer.Id);
            await _cartRepository.ClearCartAsync(cart);

            if (!result) return StatusCode(500, "Something went wrong while creating the order");

            return CreatedAtAction(nameof(GetOrderById), new { orderId = order.Id }, order);
        }
    }
}