﻿using InventoryManagementSystem.Models.EF;
using InventoryManagementSystem.Models.ResultModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace InventoryManagementSystem.Controllers.Api
{
    [Route("api/payment")]
    [ApiController]
    public class PaymentApiController : ControllerBase
    {
        private readonly InventoryManagementSystemContext _dbContext;

        public PaymentApiController(InventoryManagementSystemContext dbContext)
        {
            _dbContext = dbContext;
        }

        /* method: GET
         * 
         * url: api/payment
         * 
         * input: none
         * 
         * output: A JSON object having the same structure 
         *         as PaymentResultModel class.
         * 
         * Note: A user can only get their payment info; while
         *       an admin can get everyone's.
         */
        // 取得 Payment 資訊
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetPayment()
        {
            bool isAdmin = User.HasClaim(ClaimTypes.Role, "admin");

            IQueryable<PaymentOrder> paymentOrders = _dbContext.PaymentOrders;

            if(!isAdmin)
            {
                string userIdString = User.Claims
                    .FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)
                    .Value;

                Guid userId = Guid.Parse(userIdString);

                paymentOrders = paymentOrders
                    .Where(po => po.Order.UserId == userId);
            }

            PaymentResultModel[] payments = await paymentOrders
                    .Select(po => new PaymentResultModel
                    {
                        PaymentId = po.PaymentId,
                        RentalFee = po.Payment.RentalFee,
                        ExtraFee = po.Payment.ExtraFee,

                        Orders = po.Payment.PaymentOrders
                            .Select(po => new OrderInPaymentResultModel
                            {
                                OrderId = po.OrderId,
                                Quantity = po.Order.Quantity,
                                OrderTime = po.Order.OrderTime,
                                EquipmentSn = po.Order.Equipment.EquipmentSn,
                                EquipmentName = po.Order.Equipment.EquipmentName,
                                Brand = po.Order.Equipment.Brand,
                                Model = po.Order.Equipment.Brand,
                                Price = po.Order.Quantity * po.Order.Day * po.Order.Equipment.UnitPrice
                            })
                            .ToArray(),

                        PaymentDetails = po.Payment.PaymentDetails
                            .Select(pd => new PaymentDetailResultModel
                            {
                                PaymentDetailId = pd.PaymentDetailId,
                                AmountPaid = pd.AmountPaid,
                                PayTime = pd.PayTime
                            })
                            .ToArray()
                    })
                    .ToArrayAsync();

            return Ok(payments);
        }

        /* method: POST
         * 
         * url: api/payment/new
         * 
         * intput: A JSON containing an array of OrderIDs.
         * 
         * output:
         * 
         */
        // 使用者選取 Orders，準備付款。
        [HttpPost("new")]
        [Authorize(Roles = "user")]
        public async Task<IActionResult> MakeNewPayment(Guid[] ids)
        {
            #region 訂單合法且屬於本人
            string userIdString = User.Claims
                .FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)
                .Value;

            Guid userId = Guid.Empty;
            userId = Guid.Parse(userIdString);

            Guid[] distinctOIDs = ids.Distinct().ToArray();
            if(distinctOIDs.Length != ids.Length)
            {
                return BadRequest("訂單編號有重覆");
            }

            // distinctOIDs 只拿來檢查 ids 是否有重覆
            // 只要能執行到這邊，保證兩個 array 的 elements 都一致
            // 為了不產生混淆，以下一律採用 ids
            Order[] orders = await _dbContext.Orders
                .Where(o => ids.Contains(o.OrderId))
                .Where(o => o.OrderStatusId == "A")
                .Where(o => o.PaymentOrder == null)
                .Where(o => o.EstimatedPickupTime > DateTime.Now)
                .ToArrayAsync();

            // 訂單不合法
            if(orders.Length != ids.Length)
                return BadRequest("有訂單不可執行付款或不存在");

            bool belongToTheUser = orders.All(o => o.UserId == userId);

            // 訂單不屬於本人
            if(!belongToTheUser)
                return BadRequest("有訂單不屬於本人");
            #endregion

            #region 新增 Payment
            var pricesQry = await (from eq in _dbContext.Equipment
                                   join o in _dbContext.Orders on eq.EquipmentId equals o.EquipmentId
                                   where ids.Contains(o.OrderId)
                                   select eq.UnitPrice * o.Day * o.Quantity)
                             .ToArrayAsync();

            var totalPrice = pricesQry
                .Aggregate((total, next) => total + next);

            Guid paymentId = Guid.NewGuid();
            Payment payment = new Payment
            {
                PaymentId = paymentId,
                RentalFee = totalPrice,
                ExtraFee = 0
            };
            _dbContext.Payments.Add(payment);
            #endregion

            #region 新增 PaymentLog
            PaymentLog pLog = new PaymentLog
            {
                PaymentLogId = Guid.NewGuid(),
                PaymentId = paymentId,
                Fee = totalPrice,
                FeeCategoryId = "R", // Rental fee
                Description = string.Empty
            };
            _dbContext.PaymentLogs.Add(pLog);
            #endregion

            #region 新增 PaymentOrder （一對多關聯表）
            foreach(Order order in orders)
            {
                PaymentOrder po = new PaymentOrder
                {
                    PaymentId = paymentId,
                    OrderId = order.OrderId
                };
                _dbContext.PaymentOrders.Add(po);
            }
            #endregion

            // TODO 串金流
            #region 新增 PaymentDetail
            PaymentDetail pd = new PaymentDetail
            {
                PaymentDetailId = Guid.NewGuid(),
                PaymentId = paymentId,
                AmountPaid = totalPrice,
                PayTime = DateTime.Now
            };
            _dbContext.PaymentDetails.Add(pd);
            #endregion

            #region 更新資料庫
            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch
            {
                return Conflict();
            }
            #endregion

            return Ok();
        }
    }
}
