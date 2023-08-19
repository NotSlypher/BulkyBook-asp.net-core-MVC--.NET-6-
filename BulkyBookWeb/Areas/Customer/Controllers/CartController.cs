using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using BulkyBook.DataAccess.Repository.IRepository;
using BulkyBook.Models;
using BulkyBook.Models.ViewModels;
using BulkyBook.Utility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Stripe.Checkout;
using static System.Net.WebRequestMethods;

namespace BulkyBookWeb.Areas.Customer.Controllers
{
	[Area("Customer")]
	[Authorize]
	public class CartController : Controller
	{
		private readonly IUnitOfWork _unitOfWork;
		private readonly IEmailSender _emailSender;
		[BindProperty]
		public ShoppingCartVM ShoppingCartVM { get; set; }
		public int OrderTotal { get; set; }

		public CartController(IUnitOfWork unitOfWork, IEmailSender emailSender)
		{
			_unitOfWork = unitOfWork;
			_emailSender = emailSender;
		}

		public IActionResult OrderConfirmation(int id)
		{
			OrderHeader orderHeader = _unitOfWork.OrderHeader.GetFirstOrDefault(u => u.Id == id, includeProperties:"ApplicationUser");
			if (orderHeader.PaymentStatus != SD.PaymentStatusDelayedPayment)
			{
				var service = new SessionService();
				Session session = service.Get(orderHeader.SessionId);
				if (session.PaymentStatus.ToLower() == "paid")
				{
					_unitOfWork.OrderHeader.UpdateStripePaymentId(id, orderHeader.SessionId, session.PaymentIntentId);
					_unitOfWork.OrderHeader.UpdateStatus(id, SD.StatusApproved, SD.PaymentStatusApproved);
					_unitOfWork.Save();
				}
			}

            var claimsIdentity = (ClaimsIdentity)User.Identity;
            var claim = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier);

            ShoppingCartVM = new ShoppingCartVM()
            {
                ListCart = _unitOfWork.ShoppingCart.GetAll(u => u.ApplicationUserId == claim.Value, includeProperties: "Product"),
                OrderHeader = new()
            };
            foreach (var cart in ShoppingCartVM.ListCart)
            {
                cart.Price = GetPriceBasedOnQuantity(cart.Count, cart.Product.Price, cart.Product.Price50, cart.Product.Price100);
                ShoppingCartVM.OrderHeader.OrderTotal += (cart.Price * cart.Count);
            }

            string body =
                "<h1>Order Confirmation</h1>\r\n\t<p>Dear " + orderHeader.ApplicationUser.Name + ",</p>\r\n\t<p>Thank you for your recent purchase with us. We are pleased to confirm that your order has been received and is being processed. Here are the details of your order:</p>\r\n\t<table>\r\n\t\t<tr>\r\n\t\t\t<th></th>\r\n\t\t\t<th>Product</th>\r\n\t\t\t<th>Quantity</th>\r\n\t\t\t<th>Price</th>\r\n\t\t</tr>";
            foreach (var cart in ShoppingCartVM.ListCart)
            {
                body += "\r\n\t\t<tr>\r\n\t\t\t<td> <img src=\"" + cart.Product.ImageUrl + "\"/></td>\r\n\t\t\t<td>" + cart.Product.Title + "</td>\r\n\t\t\t<td>" + cart.Count + "</td>\r\n\t\t\t<td>" + cart.Price * cart.Count + "</td>\r\n\t\t</tr>";
            }

            body +=
                "\r\n\t\t<tr>\r\n\t\t\t<td></td>\r\n\t\t\t<td colspan=\"2\">Total:</td>\r\n\t\t\t<td>" + ShoppingCartVM.OrderHeader.OrderTotal + "</td>\r\n\t\t</tr>\r\n\t</table>\r\n\t<p>Your order will be shipped to the following address:</p>\r\n\t<p>" + orderHeader.ApplicationUser.StreetAddress + ", " + orderHeader.ApplicationUser.City + " " + orderHeader.ApplicationUser.PostalCode + ", " + orderHeader.ApplicationUser.State + "</p>\r\n\t<p>If you have any questions or concerns regarding your order, please do not hesitate to contact us.</p>\r\n\t<p>Thank you for choosing our company, and we look forward to serving you again in the future.</p>\r\n\t<p>Sincerely,</p>\r\n\t<p>The BulkyBook Team</p>";
            _emailSender.SendEmailAsync(orderHeader.ApplicationUser.Email, "New Order - BulkyBook", body);
			List<ShoppingCart> shoppingCarts = _unitOfWork.ShoppingCart
				.GetAll(u => u.ApplicationUserId == orderHeader.ApplicationUserId).ToList();
            HttpContext.Session.Clear();
			_unitOfWork.ShoppingCart.RemoveRange(shoppingCarts);
			_unitOfWork.Save();
			return View(id);
		}

		public IActionResult Plus(int cartId)
		{
			var cart = _unitOfWork.ShoppingCart.GetFirstOrDefault(u => u.Id == cartId);
			_unitOfWork.ShoppingCart.IncrementCount(cart, 1);
			_unitOfWork.Save();
			return RedirectToAction(nameof(Index));
		}

		public IActionResult Minus(int cartId)
		{
			var cart = _unitOfWork.ShoppingCart.GetFirstOrDefault(u => u.Id == cartId);
            if (cart.Count == 1)
            {
                _unitOfWork.ShoppingCart.Remove(cart); 
                var count = _unitOfWork.ShoppingCart.GetAll(u => u.ApplicationUserId == cart.ApplicationUserId).ToList().Count - 1;
                HttpContext.Session.SetInt32(SD.SessionCart, count);
            }
            else
				_unitOfWork.ShoppingCart.DecrementCount(cart, 1);
			_unitOfWork.Save();
			return RedirectToAction(nameof(Index));
		}

		public IActionResult Remove(int cartId)
		{
			var cart = _unitOfWork.ShoppingCart.GetFirstOrDefault(u => u.Id == cartId);
			_unitOfWork.ShoppingCart.Remove(cart);
			_unitOfWork.Save();
            var count = _unitOfWork.ShoppingCart.GetAll(u => u.ApplicationUserId == cart.ApplicationUserId).ToList().Count;
			HttpContext.Session.SetInt32(SD.SessionCart, count);
			return RedirectToAction(nameof(Index));
		}

		public IActionResult Index()
        {
			var claimsIdentity = (ClaimsIdentity)User.Identity;
			var claim = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier);

			try
			{
				ShoppingCartVM = new ShoppingCartVM()
				{
					ListCart = _unitOfWork.ShoppingCart.GetAll(u => u.ApplicationUserId == claim.Value, includeProperties: "Product"),
					OrderHeader = new()
				};
				foreach (var cart in ShoppingCartVM.ListCart)
				{
					cart.Price = GetPriceBasedOnQuantity(cart.Count, cart.Product.Price, cart.Product.Price50, cart.Product.Price100);
					ShoppingCartVM.OrderHeader.OrderTotal += (cart.Price * cart.Count);
				}
                return View(ShoppingCartVM);
			}
			catch (System.Exception exception)
			{

				return null;
			}
		}

		public IActionResult Summary()
		{
			var claimsIdentity = (ClaimsIdentity)User.Identity;
			var claim = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier);

			ShoppingCartVM = new ShoppingCartVM()
			{
				ListCart = _unitOfWork.ShoppingCart.GetAll(u => u.ApplicationUserId == claim.Value, includeProperties: "Product"),
				OrderHeader = new()
			};

			ShoppingCartVM.OrderHeader.ApplicationUser =
				_unitOfWork.ApplicationUser.GetFirstOrDefault(u => u.Id == claim.Value);

			ShoppingCartVM.OrderHeader.Name = ShoppingCartVM.OrderHeader.ApplicationUser.Name;
			ShoppingCartVM.OrderHeader.City = ShoppingCartVM.OrderHeader.ApplicationUser.City;
			ShoppingCartVM.OrderHeader.PhoneNumber = ShoppingCartVM.OrderHeader.ApplicationUser.PhoneNumber;
			ShoppingCartVM.OrderHeader.StreetAddress = ShoppingCartVM.OrderHeader.ApplicationUser.StreetAddress;
			ShoppingCartVM.OrderHeader.State = ShoppingCartVM.OrderHeader.ApplicationUser.State;
			ShoppingCartVM.OrderHeader.PostalCode = ShoppingCartVM.OrderHeader.ApplicationUser.PostalCode;

			foreach (var cart in ShoppingCartVM.ListCart)
			{
				cart.Price = GetPriceBasedOnQuantity(cart.Count, cart.Product.Price, cart.Product.Price50, cart.Product.Price100);
				ShoppingCartVM.OrderHeader.OrderTotal += (cart.Price * cart.Count);
			}
			return View(ShoppingCartVM);
		}
		[HttpPost]
		[ActionName("Summary")]
		[ValidateAntiForgeryToken]
		public IActionResult SummaryPost()
		{
			var claimsIdentity = (ClaimsIdentity)User.Identity;
			var claim = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier);

			ShoppingCartVM.ListCart =
				_unitOfWork.ShoppingCart.GetAll(u => u.ApplicationUserId == claim.Value, includeProperties: "Product");

			ShoppingCartVM.OrderHeader.OrderDate = System.DateTime.Now;
			ShoppingCartVM.OrderHeader.ApplicationUserId = claim.Value;

			foreach (var cart in ShoppingCartVM.ListCart)
			{
				cart.Price = GetPriceBasedOnQuantity(cart.Count, cart.Product.Price, cart.Product.Price50, cart.Product.Price100);
				ShoppingCartVM.OrderHeader.OrderTotal += (cart.Price * cart.Count);
			}

			ApplicationUser applicationUser = _unitOfWork.ApplicationUser.GetFirstOrDefault(u => u.Id == claim.Value);
			if (applicationUser.CompanyId.GetValueOrDefault() == 0)
			{
				ShoppingCartVM.OrderHeader.PaymentStatus = SD.PaymentStatusPending;
				ShoppingCartVM.OrderHeader.OrderStatus = SD.StatusPending;
			}
			else
			{
				ShoppingCartVM.OrderHeader.PaymentStatus = SD.PaymentStatusDelayedPayment;
				ShoppingCartVM.OrderHeader.OrderStatus = SD.StatusApproved;
			}

			_unitOfWork.OrderHeader.Add(ShoppingCartVM.OrderHeader);
			_unitOfWork.Save();
			foreach (var cart in ShoppingCartVM.ListCart)
			{
				OrderDetail orderDetail = new()
				{
					ProductId = cart.ProductId,
					OrderId = ShoppingCartVM.OrderHeader.Id,
					Price = cart.Price,
					Count = cart.Count,
				};
				_unitOfWork.OrderDetail.Add(orderDetail);
				_unitOfWork.Save();
			}

			//Stripe Setting
			if (applicationUser.CompanyId.GetValueOrDefault() == 0)
			{
				var domain = "https://bulkydata.azurewebsites.net/";
				var options = new SessionCreateOptions
				{
					PaymentMethodTypes = new List<string>
					{
						"card",
					},
					LineItems = new List<SessionLineItemOptions>(),
					Mode = "payment",
					SuccessUrl = domain + $"customer/cart/OrderConfirmation?id={ShoppingCartVM.OrderHeader.Id}",
					CancelUrl = domain + $"customer/cart/index",
				};

				foreach (var item in ShoppingCartVM.ListCart)
				{

					var sessionLineItem = new SessionLineItemOptions
					{
						PriceData = new SessionLineItemPriceDataOptions
						{
							UnitAmount = (long)(item.Price * 100),
							Currency = "inr",
							ProductData = new SessionLineItemPriceDataProductDataOptions
							{
								Name = item.Product.Title
							},
						},
						Quantity = item.Count,
					};
					options.LineItems.Add(sessionLineItem);
				}

				var service = new SessionService();
				Session session = service.Create(options);
				_unitOfWork.OrderHeader.UpdateStripePaymentId(ShoppingCartVM.OrderHeader.Id, session.Id,
					session.PaymentIntentId);
				_unitOfWork.Save();
				Response.Headers.Add("Location", session.Url);
				return new StatusCodeResult(303);
			}

			else
			{
				return RedirectToAction("OrderConfirmation", "Cart", new { id = ShoppingCartVM.OrderHeader.Id });
			}
			// _unitOfWork.ShoppingCart.RemoveRange(ShoppingCartVM.ListCart);
			// _unitOfWork.Save();
			// return RedirectToAction("Index", "Home");
		}

		private double GetPriceBasedOnQuantity(double quantity, double price, double price50, double price100)
		{
			if(quantity < 50) return price;
			else if(quantity < 100) return price50;
			else return price100;
		}
	}
}
