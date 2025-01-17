﻿using InventoryManagementSystem.Models.EF;
using InventoryManagementSystem.Models.LINE;
using InventoryManagementSystem.Models.NotificationModels;
using InventoryManagementSystem.Models.Password;
using InventoryManagementSystem.Models.reCAPTCHA;
using InventoryManagementSystem.Models.ResultModels;
using InventoryManagementSystem.Models.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace InventoryManagementSystem.Controllers.Api
{
    [Route("api/user")]
    [ApiController]
    public class UserApiController : ControllerBase
    {
        private readonly InventoryManagementSystemContext _dbContext;
        private readonly NotificationService notificationService;
        private readonly LineConfig _lineConfig;
        private readonly IConfiguration Config;

        public UserApiController(
            InventoryManagementSystemContext dbContext,
            IOptions<LineConfig> lineConfig,
            NotificationService notificationService, 
            IConfiguration config)
        {
            _dbContext = dbContext;
            this.notificationService = notificationService;
            _lineConfig = lineConfig.Value;
            Config = config;
        }

        /* method: GET
         * 
         * url: api/user/validate?validatedField={FieldName}&value={Value}
         * 
         * input: FieldName accepts 3 values: 'username', 'email', 'phoneNumber'.
         * 
         * output: True if the field value is available;
         *         False if the field value is not available.
         */
        // 驗證 username、email、phoneNumber 是否可被註冊
        [HttpPost("validate")]
        public async Task<IActionResult> ValidateUser([FromForm] string validatedField, [FromForm] string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return Ok(false);
            }

            bool userExists;
            switch (validatedField)
            {
                case "username":
                    userExists = await _dbContext.Users.AnyAsync(u => u.Username == value);
                    break;
                case "email":
                    userExists = await _dbContext.Users.AnyAsync(u => u.Email == value);
                    break;
                case "phoneNumber":
                    userExists = await _dbContext.Users.AnyAsync(u => u.PhoneNumber == value);
                    break;
                default:
                    return Ok(false);
            }

            if (userExists)
                return Ok(false);

            return Ok(true);
        }

        /* method:  GET
         * 
         * url:     1. /api/user/
         *                          For admins, this returns all users' info
         *                          For users, this returns their own info
         *          2. /api/user/{UserID}
         *                          For admins, this returns the specific user's info
         * 
         * input: A user's id or null
         * 
         * output: A JSON array containing one or more serialized GetUserResultModel objects.
         */
        [HttpGet("{id?}")]
        [Authorize]
        public async Task<IActionResult> GetUser(Guid? id)
        {
            bool isAdmin = User.HasClaim(ClaimTypes.Role, "admin");

            IQueryable<User> userQry = _dbContext.Users;

            if (!isAdmin)
            {
                // If it's a user,
                string userIdString = User.Claims
                    .FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)
                    .Value;
                Guid userId = Guid.Parse(userIdString);

                // then they only get their own info.
                userQry = userQry.Where(u => u.UserId == userId);
            }
            else
            {
                // If it's an admin, they can get all users' info 
                // by leaving the id null.


                // Or they can specify the id of the user they would like
                // to get info about.
                if (id != null)
                    userQry = userQry.Where(u => u.UserId == id);
            }

            GetUserResultModel[] users = await userQry
                .Select(u => new GetUserResultModel
                {
                    UserId = u.UserId,
                    UserSn = u.UserSn,
                    Username = u.Username,
                    Email = u.Email,
                    FullName = u.FullName,
                    AllowNotification = u.AllowNotification,
                    Address = u.Address,
                    PhoneNumber = u.PhoneNumber,
                    Gender = u.Gender,
                    DateOfBirth = u.DateOfBirth,
                    CreateTime = u.CreateTime,
                    ViolationTimes = u.ViolationTimes,
                    Banned = u.Banned,
                    LineEnabled = !string.IsNullOrWhiteSpace(u.LineId)
                })
                .ToArrayAsync();

            return Ok(users);
        }
        /* method: POST
         * 
         * url: api/user/
         * 
         * input: A JSON object having the same structure as PostUserViewModel
         *        in which Username, Email, Password, Fullname and PhoneNumber
         *        are required.
         * 
         * output: 1. Redirect (Found 302) to Equip page if success.
         *         2. Bad Request 400 if any required field is empty.
         *         3. Conflict 409 if failing to update the database.
         */
        // 註冊用 API
        [HttpPost]
        [Consumes("application/json")]
        public async Task<IActionResult> PostUser(PostUserViewModel model)
        {
            #region 簡查 Required Field 是否都有填
            string[] notNullFields =
            {
                model.Username,
                model.Email,
                model.Password,
                model.FullName,
                model.PhoneNumber
            };

            bool nullOrWhiteSpaceExist = notNullFields
                .Any(f => string.IsNullOrWhiteSpace(f));

            if (nullOrWhiteSpaceExist)
            {
                return BadRequest("有必填欄位為空");
            }
            #endregion

            #region Hash Password
            PBKDF2 hasher = new PBKDF2(model.Password, null);
            #endregion
            //To be removed
            //IHashPassword hasher = this as IHashPassword;
            //Random r = new Random();
            //byte[] saltBytes = new byte[32];
            //byte[] passwordBytes = Encoding.UTF8.GetBytes(model.Password);
            //r.NextBytes(saltBytes);
            //byte[] hashedPassword = hasher.HashPasswordWithSalt(passwordBytes, saltBytes);

            User user = new User
            {
                UserId = Guid.NewGuid(),
                Username = model.Username,
                Email = model.Email,
                HashedPassword = hasher.HashedPassword,
                Salt = hasher.Salt,
                FullName = model.FullName,
                AllowNotification = model.AllowNotification,
                Address = model.Address,
                PhoneNumber = model.PhoneNumber,
                Gender = model.Gender,
                DateOfBirth = model.DateOfBirth,
                CreateTime = DateTime.Now
            };

            _dbContext.Users.Add(user);

            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch
            {
                return Conflict("資料庫更新失敗");
            }

            // 註冊成功後直接發 cookie，視同登入。
            List<Claim> claims = new List<Claim>();
            claims.Add(new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()));
            claims.Add(new Claim(ClaimTypes.Name, user.Username));
            claims.Add(new Claim(ClaimTypes.Role, "user"));
            ClaimsIdentity identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            ClaimsPrincipal principal = new ClaimsPrincipal(identity);
            await HttpContext.SignInAsync(principal);
            return RedirectToAction("EquipQry", "Equips");

        }

        /* PUT
         * api/user/{UserID}
         * 
         * input: A JSON object having the same structure as PutUserViewModel
         *        in which Username, Email, OldPassword, Fullname and PhoneNumber
         *        are required.
         *        Note that Password here means the new one, while OldPassword
         *        means, obviously, the old one.
         * 
         * output: 1. Ok 200 if success.
         *         2. Unauthorized 401 if you are not an admin or the owner of the account.
         *         3. Bad Request 400 if any required field is empty or OldPassword is wrong.
         *         4. Conflict 409 if failing to update the database.
         *         
         * Note that PutUserViewModel is a subclass of PostUserViewModel.
         * 
         */
        // 修改 User 資訊的 API。Admin 可改所有 User 的資訊；而 User 只能改自己的。
        [HttpPut("{id}")]
        [Consumes("application/json")]
        [Authorize]
        public async Task<IActionResult> PutUser(Guid id, PutUserViewModel model)
        {
            if (id != model.UserId)
            {
                return BadRequest("欲修改之 User ID 與實際傳入 ID 不同");
            }

            #region 檢查目前正在修改的人是否為管理員或本人
            // Admin 可以改所有 User 的資料
            bool isAdmin = User.HasClaim(ClaimTypes.Role, "admin");

            // User 只能改自己的資料
            if (!isAdmin)
            {
                string userIdString = User.Claims
                    .FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)
                    .Value;

                Guid userId = Guid.Parse(userIdString);

                if(userId != id)
                    return Unauthorized("只能修改本人資料");
            }
            #endregion

            #region 檢查是否所有 required field 是都有填寫
            List<string> notNullFields = new List<string>
            {
                model.Username,
                model.Email,
                model.FullName,
                model.PhoneNumber
            };

            // 管理員修改時不需填寫密碼
            // 只有使用者修改時需要填
            if (!isAdmin)
                notNullFields.Add(model.OldPassword);

            bool nullOrWhiteSpaceExist = notNullFields
                .Any(f => string.IsNullOrWhiteSpace(f));

            if (nullOrWhiteSpaceExist)
            {
                return BadRequest();
            }
            #endregion

            #region The mapping process between the view model and the EF model
            User user = await _dbContext.Users.FindAsync(id);

            // 不是管理員就必須填入正確的密碼

            if(!isAdmin)
            {
                PBKDF2 hasher = new PBKDF2(model.OldPassword, user.Salt);

                if(!hasher.HashedPassword.SequenceEqual(user.HashedPassword))
                    return BadRequest("密碼錯誤");
            }

            // 有輸入新密碼才修改密碼
            if (model.Password != null)
            {
                PBKDF2 hasher = new PBKDF2(model.Password, null);

                user.Salt = hasher.Salt;
                user.HashedPassword = hasher.HashedPassword;
            }

            // 修改其他個資
            user.Email = model.Email;
            user.PhoneNumber = model.PhoneNumber;
            user.Username = model.Username;
            user.FullName = model.FullName;
            user.AllowNotification = model.AllowNotification;
            user.Address = model.Address;
            user.Gender = model.Gender;
            user.DateOfBirth = model.DateOfBirth;

            if(!model.LineEnabled && !string.IsNullOrWhiteSpace(user.LineId))
            {
                string lineText = "您已解除 LINE 綁定，將不再收到 LINE 通知。";
                await notificationService.SendLineNotification(user.LineId, lineText, user.UserId);
                user.LineId = string.Empty;
            }
            #endregion

            #region Update the database
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

        [HttpPost("checkunbound")]
        public async Task<IActionResult> CheckUnbound([FromForm] string idToken)
        {
            string lineID = string.Empty;

            #region Verify ID Token
            using (HttpClient client = new HttpClient())
            {
                client.BaseAddress = new Uri("https://api.line.me/oauth2/v2.1/verify");
                HttpContent content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    {"id_token", idToken},
                    {"client_id", _lineConfig.LIFFID}
                });
                HttpResponseMessage response = await client.PostAsync("", content);

                if (!response.IsSuccessStatusCode)
                {
                    string failJsonString = await response.Content.ReadAsStringAsync();
                    var failData = JsonConvert.DeserializeObject<VerifyIDTokenFailResponse>(failJsonString);
                    //return BadRequest();
                    return BadRequest($"{failData.error}, {failData.error_description}");
                }
                string jsonString = await response.Content.ReadAsStringAsync();
                var verifiedData = JsonConvert.DeserializeObject<VerifyIDTokenResponse>(jsonString);

                // verifiedData.sub is the line id.
                lineID = verifiedData.sub;
            }
            #endregion

            User user = await _dbContext.Users
                .Where(u => u.LineId == lineID)
                .FirstOrDefaultAsync();

            if (user != null)
                return Ok(false);
            else
                return Ok(true);

        }

        [HttpPost("bindline")]
        [Consumes("application/json")]
        public async Task<IActionResult> BindLine(BindLineViewModel model)
        {

            string lineID = string.Empty;

            #region Verify ID Token
            using (HttpClient client = new HttpClient())
            {
                client.BaseAddress = new Uri("https://api.line.me/oauth2/v2.1/verify");
                HttpContent content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    {"id_token", model.IDToken},
                    {"client_id", _lineConfig.LIFFID}
                });
                HttpResponseMessage response = await client.PostAsync("", content);

                if (!response.IsSuccessStatusCode)
                {
                    string failJsonString = await response.Content.ReadAsStringAsync();
                    var failData = JsonConvert.DeserializeObject<VerifyIDTokenFailResponse>(failJsonString);
                    return BadRequest($"{failData.error}, {failData.error_description}");
                }

                string jsonString = await response.Content.ReadAsStringAsync();
                var verifiedData = JsonConvert.DeserializeObject<VerifyIDTokenResponse>(jsonString);

                // verifiedData.sub is the line id.
                lineID = verifiedData.sub;
            }
            #endregion

            #region Authentication
            User user = await _dbContext.Users
                .Where(u => u.Username == model.Username)
                .FirstOrDefaultAsync();

            if (user == null)
                return Unauthorized("您輸入的帳號或密碼有誤");

            PBKDF2 hasher = new PBKDF2(model.Password, user.Salt);

            if(!hasher.HashedPassword.SequenceEqual(user.HashedPassword))
                return Unauthorized("您輸入的帳號或密碼有誤");
            #endregion

            #region Check if the user already has a LineID
            if (!string.IsNullOrWhiteSpace(user.LineId))
                return BadRequest("很抱歉，綁定失敗。這個帳號已經綁定過 LINE 了，如要重新綁定請先取消原本的綁定。");
            #endregion

            #region Check if the LineID exists
            bool idExists = await _dbContext.Users
                .AnyAsync(u => u.LineId == lineID);
            if (idExists)
                return BadRequest("很抱歉，綁定失敗。這個 LINE 帳號已被綁定過，如要重新綁定請先取消原本的綁定。");
            #endregion


            user.LineId = lineID;

            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch
            {
                return Conflict();
            }

            using (HttpClient client = new HttpClient())
            {
                client.BaseAddress = new Uri("https://api.line.me/v2/bot/message/push");
                client.DefaultRequestHeaders
                    .Accept
                    .Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders
                    .Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _lineConfig.AccessToken);

                PushMessage message = new PushMessage
                {
                    to = lineID,
                    messages = new LineMessage[]
                    {
                        new LineMessage
                        {
                            type = "text",
                            text = $"恭喜您！\n您已完成 LINE 綁定，以後可以直接在 LINE 上收到本站的消息唷！"
                        }
                    }
                };

                HttpContent content = JsonContent.Create<PushMessage>(message);

                await client.PostAsync("", content);
            }

            return Ok();
        }

        [HttpPost("unbindline")]
        public async Task<IActionResult> UnbindLine([FromForm] string idToken)
        {
            string lineID = string.Empty;
            #region Verify ID Token
            using (HttpClient client = new HttpClient())
            {
                client.BaseAddress = new Uri("https://api.line.me/oauth2/v2.1/verify");
                HttpContent content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    {"id_token", idToken},
                    {"client_id", _lineConfig.LIFFID}
                });
                HttpResponseMessage response = await client.PostAsync("", content);

                if (!response.IsSuccessStatusCode)
                {
                    string failJsonString = await response.Content.ReadAsStringAsync();
                    var failData = JsonConvert.DeserializeObject<VerifyIDTokenFailResponse>(failJsonString);
                    return BadRequest($"{failData.error}, {failData.error_description}");
                }

                string jsonString = await response.Content.ReadAsStringAsync();
                var verifiedData = JsonConvert.DeserializeObject<VerifyIDTokenResponse>(jsonString);

                // verifiedData.sub is the line id.
                lineID = verifiedData.sub;
            }
            #endregion

            User user = await _dbContext.Users
                .Where(u => u.LineId == lineID)
                .FirstOrDefaultAsync();

            if (user == null)
            {
                return BadRequest("這個 LINE 帳號目前沒有被綁定");
            }

            user.LineId = null;

            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch
            {
                return Conflict("解除綁定失敗");
            }

            using (HttpClient client = new HttpClient())
            {

                PushMessage message = new PushMessage
                {
                    to = lineID,
                    messages = new LineMessage[]
                    {
                        new LineMessage
                        {
                            type = "text",
                            text = $"您已解除 LINE 綁定，將不再收到 LINE 通知。"
                        }
                    }
                };

                HttpContent content = JsonContent.Create<PushMessage>(message);

                HttpRequestMessage request = new HttpRequestMessage();
                request.Method = new HttpMethod("POST");
                request.RequestUri = new Uri("https://api.line.me/v2/bot/message/push");
                request.Headers
                    .Accept
                    .Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _lineConfig.AccessToken);
                request.Content = content;

                await client.SendAsync(request);

                return Ok();
            }
        }

        [HttpPost("sendresetlink")]
        public async Task<IActionResult> SendResetLink([FromForm] string email, [FromForm] string recaptchatoken)
        {


            var recaptchaConfig = Config.GetSection("reCAPTCHA").Get<reCAPTCHAConfig>();

            HttpRequestMessage request = new HttpRequestMessage();
            request.Method = new HttpMethod("POST");
            request.RequestUri = new Uri("https://www.google.com/recaptcha/api/siteverify");
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                {"secret", recaptchaConfig.SecretKey },
                {"response", recaptchatoken }
            });
            using(HttpClient client = new HttpClient())
            {
                HttpResponseMessage response = await client.SendAsync(request);

                if(!response.IsSuccessStatusCode)
                    return BadRequest();

                string responseDataString = await response.Content.ReadAsStringAsync();
                var responseData = JsonConvert.DeserializeObject<reCAPTCHAValidationResponse>(responseDataString);

                if(!responseData.success)
                    return BadRequest();
            }

            #region 確認此 User 真的存在
            var user = await _dbContext.Users
                .Where(u => u.Email == email)
                .Select(u => new
                {
                    u.UserId,
                    u.Username,
                    u.FullName,
                    u.Email
                })
                .FirstOrDefaultAsync();

            if(user == null)
            {
                return Ok();
            }
            #endregion

            #region 先把此 User 先前產生的 Token 移除
            var rpts = await _dbContext.ResetPasswordTokens
                .Where(rpt => rpt.UserId == user.UserId)
                .ToArrayAsync();

            _dbContext.ResetPasswordTokens.RemoveRange(rpts);
            #endregion

            string token;
            byte[] hashedToken;
            #region 產生有時效性的 Token
            using(RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider())
            {
                byte[] tokenBytes = new byte[128];
                rng.GetBytes(tokenBytes);

                string base64 = Convert.ToBase64String(tokenBytes);
                token = HttpUtility.UrlEncode(base64);

                hashedToken = SHA512.HashData(tokenBytes);
            }
            #endregion

            #region 在 ResetPasswordToken Table 新增一筆資料記錄此 Token
            ResetPasswordToken resetPasswordToken = new ResetPasswordToken
            {
                UserId = user.UserId,
                ExpireTime = DateTime.Now.AddMinutes(15),
                HashedToken = hashedToken
            };

            _dbContext.ResetPasswordTokens.Add(resetPasswordToken);
            #endregion

            #region 更新資料庫
            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch
            {
                return Conflict("資料庫更新失敗");
            }
            #endregion

            string resetPasswordURL = $"{Request.Scheme}://{Request.Host}/resetpassword?token={token}";
            StringBuilder builder = new StringBuilder();

            builder.Append("<p>");
            builder.AppendFormat("@{0} 您好：", user.Username);
            builder.Append("<br><br>");
            builder.AppendFormat("這是您的重設密碼連結：");
            builder.Append("<br>");
            builder.AppendFormat("<a href='{0}'>{0}</a>", resetPasswordURL);
            builder.Append("<br>");
            builder.Append("此連結有效時間為 15 分鐘。");
            builder.Append("<br>");
            builder.Append("本信為系統自動發送，請勿直接回覆此郵件。");
            builder.Append("</p>");

            string emailText = builder.ToString();

            await notificationService.SendEmailNotification(
                user.FullName,
                user.Email,
                "重設密碼",
                "html",
                emailText);

            return Ok();
        }

        [HttpPost("password/validatetoken")]
        public async Task<IActionResult> ValidateToken([FromForm] string token)
        {
            TokenValidationModel model = await TokenHandler(token);

            if(!model.IsValid)
            {
                return BadRequest("密碼重設連結格式不正確或已過期");
            }

            return Ok();
        }

        [HttpPost("password/reset")]
        public async Task<IActionResult> ResetPassword([FromForm] string token, [FromForm] string password)
        {
            #region Validate the token
            TokenValidationModel model = await TokenHandler(token);

            if(!model.IsValid)
            {
                return BadRequest("密碼重設連結格式不正確或已過期");
            }
            #endregion

            #region Hash the password
            User user = await _dbContext.Users
                .FindAsync(model.ResetPasswordToken.UserId);

            PBKDF2 hasher = new PBKDF2(password, null);

            user.HashedPassword = hasher.HashedPassword;
            user.Salt = hasher.Salt;
            #endregion

            #region Make the token expire
            _dbContext.ResetPasswordTokens.Remove(model.ResetPasswordToken);
            #endregion
            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch
            {
                return Conflict("資料庫更新失敗");
            }

            return Ok("修改成功");


        }

        private async Task<TokenValidationModel> TokenHandler(string token)
        {
            var model = new TokenValidationModel();
            byte[] tokenBytes;
            try
            {
                tokenBytes = Convert.FromBase64String(token);
            }
            catch
            {
                return model;
            }

            byte[] hashedToken = SHA512.HashData(tokenBytes);

            ResetPasswordToken userToken = await _dbContext.ResetPasswordTokens
                .Where(rpt => rpt.HashedToken.SequenceEqual(hashedToken))
                .FirstOrDefaultAsync();

            if(userToken == null || userToken.ExpireTime < DateTime.Now)
            {
                return model;
            }

            User user = await _dbContext.Users.FindAsync(userToken.UserId);

            model.IsValid = true;
            model.ResetPasswordToken = userToken;

            return model;

        }
    }
}
