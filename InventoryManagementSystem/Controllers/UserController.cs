﻿using InventoryManagementSystem.Models.EF;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace InventoryManagementSystem.Controllers
{
    public class UserController : Controller
    {
        private readonly InventoryManagementSystemContext _dbContext;

        public UserController(InventoryManagementSystemContext dbContext)
        {
            _dbContext = dbContext;
        }

        [HttpGet("login")]
        public IActionResult Login()
        {
            return View();
        }

        [HttpPost("login")]
        public async Task<IActionResult> Authenticate(string username, string password)
        {
            User user = await _dbContext.Users
                .Where(u => u.Username == username)
                .FirstOrDefaultAsync();

            if(user == null)
            {
                return View("Login");
            }

            byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
            byte[] hashedPassword = HashPasswordWithSalt(passwordBytes, user.Salt);

            if(hashedPassword.SequenceEqual(user.HashedPassword))
            {

                List<Claim> claims = new List<Claim>();
                claims.Add(new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString(), ClaimValueTypes.Integer32));
                claims.Add(new Claim(ClaimTypes.Name, user.Username));
                claims.Add(new Claim(ClaimTypes.Role, "user"));
                ClaimsIdentity identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                ClaimsPrincipal principal = new ClaimsPrincipal(identity);
                await HttpContext.SignInAsync(principal);
                return RedirectToAction("equipQryUser", "Equips");
            }
            else
            {
                return View("Login");
            }

        }

        [Authorize(Roles = "user")]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync();
            return RedirectToAction("Index", "Home");
        }

        private byte[] HashPasswordWithSalt(byte[] password, byte[] salt)
        {
            byte[] hashedResult = null;
            using(SHA256 sha256hash = SHA256.Create())
            {
                byte[] saltAndPassword = salt.Concat(password).ToArray();
                hashedResult = sha256hash.ComputeHash(saltAndPassword);
            }

            return hashedResult;
        }
    }
}
