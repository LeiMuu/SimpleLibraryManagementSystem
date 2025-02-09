using Pastel;
using System.Collections.Concurrent;
using System.Drawing;
using static SimpleLibraryManagementSystem.LibraryManager;

namespace SimpleLibraryManagementSystem
{
    #region |--- Interfaces ---|

    interface IBook
    {
        string Title { get; set; }
        bool IsCheckedOut();
        bool IsCheckedOutByUser(string userName);
        bool SetCheckedOut(string userName);
        bool SetCheckedIn(string userName);
    }
    interface IUser
    {
        string Name { get; set; }
        int GetBooksCount();
        bool IsBorrowedBook(string title);
        bool BorrowBook(string title);
        bool ReturnBook(string title);
    }
    interface IBookManager
    {
        bool AddBook(string title);
        bool RemoveBook(string title);
        Task<MethodResult<CheckoutStatus>> CheckoutBookAsync(string title, string userName);
        Task<MethodResult<CheckInStatus>> CheckInBookAsync(string title, string userName);
    }
    interface IUserManager
    {
        bool AddUser(string userName);
        bool RemoveUser(string userName);
    }

    #endregion |--- Interfaces ---|

    #region |--- Classes ---|

    class Book : IBook
    {
        internal Book(string title)
        {
            Title = title;
            CheckedOutUser = string.Empty;
        }
        public string Title { get; set; }
        private string CheckedOutUser { get; set; }
        public bool IsCheckedOut()
        {
            return !string.IsNullOrEmpty(CheckedOutUser);
        }
        public bool IsCheckedOutByUser(string userName)
        {
            return string.Equals(CheckedOutUser, userName, StringComparison.Ordinal);
        }
        public bool SetCheckedOut(string userName)
        {
            if (!IsCheckedOut())
            {
                CheckedOutUser = userName;
                return true;
            }
            return false;
        }
        public bool SetCheckedIn(string userName)
        {
            if (IsCheckedOutByUser(userName))
            {
                CheckedOutUser = string.Empty;
                return true;
            }
            return false;
        }
    }
    class User : IUser
    {
        internal User(string name)
        {
            Name = name;
            _borrowedBooks = [];
        }
        public string Name { get; set; }
        private List<string> _borrowedBooks { get; set; }
        public int GetBooksCount()
        {
            return _borrowedBooks.Count;
        }
        public bool IsBorrowedBook(string title)
        {
            return _borrowedBooks.Contains(title);
        }
        public bool BorrowBook(string title)
        {
            if (!IsBorrowedBook(title))
            {
                _borrowedBooks.Add(title);
                return true;
            }
            return false;
        }
        public bool ReturnBook(string title)
        {
            if (IsBorrowedBook(title))
            {
                return _borrowedBooks.Remove(title);
            }
            return false;
        }
    }
    partial class LibraryManager : IBookManager, IUserManager
    {
        internal LibraryManager()
        {
            Books = [];
            Users = [];
        }

        // Book and User collections
        private Dictionary<string, IBook> Books { get; set; }
        private Dictionary<string, IUser> Users { get; set; }

        // BookManager
        public bool AddBook(string title)
        {
            Books.Add(NormalizeInput(title), new Book(title));
            return true;
        }
        public bool RemoveBook(string title)
        {
            return Books.Remove(NormalizeInput(title));
        }

        public async Task<MethodResult<CheckoutStatus>> CheckoutBookAsync(string title, string userName)
        {
            var bookTitle = NormalizeInput(title);
            var borrower = NormalizeInput(userName);

            if (!Users.TryGetValue(borrower, out IUser? user))
            {
                return new MethodResult<CheckoutStatus>(false, CheckoutStatus.UserNotFound);
            }
            if (!Books.TryGetValue(bookTitle, out IBook? book))
            {
                return new MethodResult<CheckoutStatus>(false, CheckoutStatus.BookNotFound);
            }
            if (user.GetBooksCount() >= 3)
            {
                return new MethodResult<CheckoutStatus>(false, CheckoutStatus.MaxBooksReached);
            }

            var bookLock = GetLock(_bookLocks, bookTitle);
            var userLock = GetLock(_userLocks, borrower);

            await bookLock.WaitAsync();
            await userLock.WaitAsync();

            MethodResult<CheckoutStatus> result;
            try
            {
                if (!book.SetCheckedOut(borrower))
                {
                    result = new MethodResult<CheckoutStatus>(false, CheckoutStatus.BookAlreadyCheckedOut);
                }
                else if (!user.BorrowBook(bookTitle))
                {
                    book.SetCheckedIn(borrower);
                    result = new MethodResult<CheckoutStatus>(false, CheckoutStatus.AlreadyCheckedOutByUser);
                }
                else
                {
                    result = new MethodResult<CheckoutStatus>(true, CheckoutStatus.Success);
                }
            }
            finally
            {
                bookLock.Release();
                userLock.Release();
            }

            return result;
        }
        public async Task<MethodResult<CheckInStatus>> CheckInBookAsync(string title, string userName)
        {
            var bookTitle = NormalizeInput(title);
            var borrower = NormalizeInput(userName);

            if (!Users.TryGetValue(borrower, out IUser? user))
            {
                return new MethodResult<CheckInStatus>(false, CheckInStatus.UserNotFound);
            }
            if (!Books.TryGetValue(bookTitle, out IBook? book))
            {
                return new MethodResult<CheckInStatus>(false, CheckInStatus.BookNotFound);
            }

            var bookLock = GetLock(_bookLocks, bookTitle);
            var userLock = GetLock(_userLocks, borrower);

            await bookLock.WaitAsync();
            await userLock.WaitAsync();

            MethodResult<CheckInStatus> result;
            try
            {
                if (!user.ReturnBook(bookTitle))
                {
                    result = new MethodResult<CheckInStatus>(false, CheckInStatus.BookNotBorrowedByUser);
                }
                else if (!book.SetCheckedIn(borrower))
                {
                    user.BorrowBook(bookTitle);
                    result = new MethodResult<CheckInStatus>(false, CheckInStatus.CheckedOutByAnotherUser);
                }
                else
                {
                    result = new MethodResult<CheckInStatus>(true, CheckInStatus.Success);
                }
            }
            finally
            {
                bookLock.Release();
                userLock.Release();
            }

            return result;
        }

        // UserManager
        public bool AddUser(string userName)
        {
            Users.Add(NormalizeInput(userName), new User(userName));
            return true;
        }
        public bool RemoveUser(string userName)
        {
            return Users.Remove(NormalizeInput(userName));
        }

        // LibraryManager
        public MethodResult<ListBooksStatus> ListAllBooks()
        {
            if (Books.Count == 0)
            {
                return new MethodResult<ListBooksStatus>(false, ListBooksStatus.NoBooksAvailable);
            }

            Console.WriteLine("Books available in the library:".Pastel(Color.White));
            foreach (var book in Books.Values)
            {
                string bookStatus = book.IsCheckedOut() ? " (Checked out)" : string.Empty;
                Console.WriteLine($"- {book.Title}{bookStatus}".Pastel(Color.White));
            }

            return new MethodResult<ListBooksStatus>(true, ListBooksStatus.Success);
        }
        public MethodResult<SearchBookStatus> SearchBook(string title)
        {
            var bookTitle = NormalizeInput(title);

            if (!Books.TryGetValue(bookTitle, out IBook? book))
            {
                return new MethodResult<SearchBookStatus>(false, SearchBookStatus.BookNotFound);
            }

            if (book.IsCheckedOut())
            {
                return new MethodResult<SearchBookStatus>(true, SearchBookStatus.CheckedOut);
            }

            return new MethodResult<SearchBookStatus>(true, SearchBookStatus.Available);
        }
        public bool BookExists(string title)
        {
            return Books.ContainsKey(NormalizeInput(title));
        }
        public bool UserExists(string userName)
        {
            return Users.ContainsKey(NormalizeInput(userName));
        }
    }

    #endregion |--- Classes ---|

    #region |--- Library Management System Main Program ---|
    class Program
    {
        // Display menu
        private static void DisplayMenu()
        {
            // Menu options
            var menuOptions = new[]
            {
                "[User] Add User",
                "[User] Remove User",
                "[Book] Add Book",
                "[Book] Remove Book",
                "[Book] Check Out Book",
                "[Book] Check In Book",
                "[Library] List All Books",
                "[Library] Search Book",
                "[System] Exit"
            };
            Console.WriteLine($"----------Function Menu----------".Pastel(Color.Cyan));
            for (int i = 0; i < menuOptions.Length; i++)
            {
                Console.WriteLine($"{i + 1}. {menuOptions[i]}".Pastel(Color.Cyan));
            }
            Console.WriteLine($"---------------------------------".Pastel(Color.Cyan));
            Console.Write("\nSelect an option: ".Pastel(Color.Yellow));
        }

        static async Task Main(string[] args)
        {
            LibraryManager libraryManager = new LibraryManager();

            Console.WriteLine("Welcome to Simple Library Management System.\n".Pastel(Color.White));
            while (true)
            {
                DisplayMenu();

                string? userOption = Console.ReadLine();
                switch (userOption)
                {
                    case "1":
                        // Add User
                        while (true)
                        {
                            Console.Write("Enter user name: ".Pastel(Color.Yellow));
                            string addUserName = Console.ReadLine()!.Trim();

                            if (string.IsNullOrEmpty(addUserName))
                            {
                                Console.WriteLine("User name cannot be empty. Please try again.".Pastel(Color.Orange));
                                continue;
                            }

                            if (libraryManager.UserExists(addUserName))
                            {
                                Console.WriteLine("User name already exists. Cannot add duplicate user.".Pastel(Color.Red));
                                break;
                            }

                            libraryManager.AddUser(addUserName);
                            Console.WriteLine("User added successfully.".Pastel(Color.LightGreen));
                            break;
                        }
                        break;
                    case "2":
                        // Remove User
                        while (true)
                        {
                            Console.Write("Enter user name: ".Pastel(Color.Yellow));
                            string removeUserName = Console.ReadLine()!.Trim();

                            if (string.IsNullOrEmpty(removeUserName))
                            {
                                Console.WriteLine("User name cannot be empty. Please try again.".Pastel(Color.Orange));
                                continue;
                            }

                            if (!libraryManager.RemoveUser(removeUserName)) 
                            { 
                                Console.WriteLine("User not found.".Pastel(Color.Red));
                                break;
                            }

                            Console.WriteLine("User removed successfully.".Pastel(Color.LightGreen));
                            break;
                        }
                        break;
                    case "3":
                        // Add Book
                        while (true)
                        {
                            Console.Write("Enter book title: ".Pastel(Color.Yellow));
                            string? addBookTitle = Console.ReadLine()!.Trim();

                            if (string.IsNullOrEmpty(addBookTitle))
                            {
                                Console.WriteLine("Book title cannot be empty. Please try again.".Pastel(Color.Orange));
                                continue;
                            }

                            if (libraryManager.BookExists(addBookTitle))
                            {
                                Console.WriteLine("Book title already exists. Cannot add duplicate book.".Pastel(Color.Red));
                                break;
                            }

                            libraryManager.AddBook(addBookTitle);
                            Console.WriteLine("Book added successfully.".Pastel(Color.LightGreen));
                            break;
                        }
                        break;
                    case "4":
                        // Remove Book
                        while (true)
                        {
                            Console.Write("Enter book title: ".Pastel(Color.Yellow));
                            string? removeBookTitle = Console.ReadLine()!.Trim();

                            if (string.IsNullOrEmpty(removeBookTitle))
                            {
                                Console.WriteLine("Book title cannot be empty. Please try again.".Pastel(Color.Orange));
                                continue;
                            }

                            if (!libraryManager.RemoveBook(removeBookTitle))
                            {
                                Console.WriteLine("Book not found.".Pastel(Color.Red));
                                break;
                            }

                            libraryManager.RemoveBook(removeBookTitle);
                            Console.WriteLine("Book removed successfully.".Pastel(Color.LightGreen));
                            break;
                        }
                        break;
                    case "5":
                        // Check Out Book
                        while (true)
                        {
                            Console.Write("Enter book title: ".Pastel(Color.Yellow));
                            string? checkoutBookTitle = Console.ReadLine()!.Trim();
                            Console.Write("Enter user name: ".Pastel(Color.Yellow));
                            string? checkoutUserName = Console.ReadLine()!.Trim();
                            if (string.IsNullOrEmpty(checkoutBookTitle) || string.IsNullOrEmpty(checkoutUserName))
                            {
                                Console.WriteLine("Book title and user name cannot be empty. Please try again.".Pastel(Color.Orange));
                                continue;
                            }

                            // Check if user exists, if not, create user
                            if (!libraryManager.UserExists(checkoutUserName))
                            {
                                libraryManager.AddUser(checkoutUserName);
                                Console.WriteLine("User did not exist and was created successfully.".Pastel(Color.LightGreen));
                            }

                            var checkoutBookResult = await libraryManager.CheckoutBookAsync(checkoutBookTitle, checkoutUserName);

                            // Message color
                            Color CheckoutBookMessageColor = checkoutBookResult.Status switch
                            {
                                CheckoutStatus.Success => Color.LightGreen,
                                CheckoutStatus.UserNotFound => Color.Red,
                                CheckoutStatus.BookNotFound => Color.Red,
                                CheckoutStatus.BookAlreadyCheckedOut => Color.Orange,
                                CheckoutStatus.MaxBooksReached => Color.Orange,
                                CheckoutStatus.AlreadyCheckedOutByUser => Color.Orange,
                                _ => Color.Gray
                            };
                            Console.WriteLine(checkoutBookResult.Message.Pastel(CheckoutBookMessageColor));
                            break;
                        }
                        break;
                    case "6":
                        // Check In Book
                        while (true)
                        {
                            Console.Write("Enter book title: ".Pastel(Color.Yellow));
                            string? checkInBookTitle = Console.ReadLine()!.Trim();
                            Console.Write("Enter user name: ".Pastel(Color.Yellow));
                            string? checkInUserName = Console.ReadLine()!.Trim();
                            if (string.IsNullOrEmpty(checkInBookTitle) || string.IsNullOrEmpty(checkInUserName))
                            {
                                Console.WriteLine("Book title and user name cannot be empty. Please try again.".Pastel(Color.Orange));
                                continue;
                            }

                            var checkInBookResult = await libraryManager.CheckInBookAsync(checkInBookTitle, checkInUserName);

                            // Message color
                            Color checkInBookMessageColor = checkInBookResult.Status switch
                            {
                                CheckInStatus.Success => Color.LightGreen,
                                CheckInStatus.UserNotFound => Color.Red,
                                CheckInStatus.BookNotFound => Color.Red,
                                CheckInStatus.BookNotBorrowedByUser => Color.Orange,
                                CheckInStatus.CheckedOutByAnotherUser => Color.Orange,
                                _ => Color.Gray
                            };
                            Console.WriteLine(checkInBookResult.Message.Pastel(checkInBookMessageColor));
                            break;
                        }
                        break;
                    case "7":
                        // List All Books
                        var listAllBooksResult = libraryManager.ListAllBooks();

                        // Message color
                        Color listAllBooksMessageColor = listAllBooksResult.Status switch
                        {
                            ListBooksStatus.Success => Color.LightGreen,
                            ListBooksStatus.NoBooksAvailable => Color.Orange,
                            _ => Color.Gray
                        };

                        Console.WriteLine(listAllBooksResult.Message.Pastel(listAllBooksMessageColor));
                        break;
                    case "8":
                        // Search Book
                        while (true)
                        {
                            Console.Write("Enter book title: ".Pastel(Color.Yellow));
                            string? searchBookTitle = Console.ReadLine()!.Trim();

                            if (string.IsNullOrEmpty(searchBookTitle))
                            {
                                Console.WriteLine("Book title cannot be empty. Please try again.".Pastel(Color.Orange));
                                continue;
                            }

                            var searchBookResult = libraryManager.SearchBook(searchBookTitle);

                            // Message color
                            Color searchBookMessageColor = searchBookResult.Status switch
                            {
                                SearchBookStatus.Available => Color.LightGreen,
                                SearchBookStatus.CheckedOut => Color.Orange,
                                SearchBookStatus.BookNotFound => Color.Red,
                                _ => Color.Gray
                            };

                            Console.WriteLine(searchBookResult.Message.Pastel(searchBookMessageColor));
                            break;
                        }
                        break;
                    case "9":
                        // Exit
                        Console.WriteLine("Exiting the program. Goodbye!".Pastel(Color.White));
                        return;
                    default:
                        // Invalid option
                        Console.WriteLine("Invalid option. Please try again.".Pastel(Color.Orange));
                        break;
                }
            }
        }
    }

    #endregion |--- Library Management System Main Program ---|

    #region |--- LibraryManager partial classes ---|

    // Normalize input
    partial class LibraryManager
    {
        private static string NormalizeInput(string input)
        {
            return input.ToLowerInvariant();
        }
    }

    // Locks for book and user operations
    partial class LibraryManager
    {
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _bookLocks = [];
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _userLocks = [];

        private static SemaphoreSlim GetLock(ConcurrentDictionary<string, SemaphoreSlim> lockDictionary, string key)
        {
            return lockDictionary.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        }
    }

    // MethodResult
    partial class LibraryManager
    {
        public class MethodResult<TStatus>(bool success, TStatus status) where TStatus : Enum
        {
            public bool Success { get; } = success;
            public TStatus Status { get; } = status;
            public string Message => StatusMessages.GetMessage(Status);
        }
    }

    // Status messages
    partial class LibraryManager
    {
        public enum CheckoutStatus
        {
            Success,
            UserNotFound,
            BookNotFound,
            BookAlreadyCheckedOut,
            MaxBooksReached,
            AlreadyCheckedOutByUser
        }
        public enum CheckInStatus
        {
            Success,
            UserNotFound,
            BookNotFound,
            BookNotBorrowedByUser,
            CheckedOutByAnotherUser
        }
        public enum ListBooksStatus
        {
            Success,
            NoBooksAvailable
        }
        public enum SearchBookStatus
        {
            Available,
            CheckedOut,
            BookNotFound
        }

        public static class StatusMessages
        {
            public static string GetMessage<TStatus>(TStatus status) where TStatus : Enum
            {
                return status switch
                {
                    CheckoutStatus checkoutStatus => GetCheckoutMessage(checkoutStatus),
                    CheckInStatus checkInStatus => GetCheckInMessage(checkInStatus),
                    ListBooksStatus listBooksStatus => GetListBooksMessage(listBooksStatus),
                    SearchBookStatus searchBookStatus => GetSearchBookMessage(searchBookStatus),
                    _ => throw new ArgumentException($"Unsupported status type: {typeof(TStatus).Name}")
                };
            }
            private static readonly Dictionary<CheckoutStatus, string> _checkoutStatusMessages = new()
            {
                { CheckoutStatus.Success, "[System]: The book has been successfully checked out." },
                { CheckoutStatus.UserNotFound, "[UserManager]: Error! The system did not automatically create the user information. Please re-execute the check out process." },
                { CheckoutStatus.BookNotFound, "[LibraryManager]: The book is not included in this library." },
                { CheckoutStatus.BookAlreadyCheckedOut, "[BookManager]: The book is already checked out." },
                { CheckoutStatus.MaxBooksReached, "[LibraryManager]: The user has reached the maximum number of books that can be checked out." },
                { CheckoutStatus.AlreadyCheckedOutByUser, "[UserManager]: The book is already checked out by yourself." }
            };
            private static readonly Dictionary<CheckInStatus, string> _checkInStatusMessages = new()
            {
                { CheckInStatus.Success, "[System]: The book has been successfully checked in." },
                { CheckInStatus.UserNotFound, "[UserManager]: Error! The user does not exist." },
                { CheckInStatus.BookNotFound, "[LibraryManager]: The book is not included in this library." },
                { CheckInStatus.BookNotBorrowedByUser, "[BookManager]: The book is not checked out by the user." },
                { CheckInStatus.CheckedOutByAnotherUser, "[UserManager]: You cannot check in a book checked out by another user." }
            };
            private static readonly Dictionary<ListBooksStatus, string> _listBooksStatusMessages = new()
            {
                { ListBooksStatus.Success, "[System]: Books listed successfully." },
                { ListBooksStatus.NoBooksAvailable, "[LibraryManager]: No books available in the library." }
            };
            private static readonly Dictionary<SearchBookStatus, string> _searchBookStatusMessages = new()
            {
                { SearchBookStatus.Available, "[LibraryManager]: The book is available in the library." },
                { SearchBookStatus.CheckedOut, "[LibraryManager]: The book is currently checked out." },
                { SearchBookStatus.BookNotFound, "[LibraryManager]: The book is not in the library." }
            };

            private static string GetCheckoutMessage(CheckoutStatus status)
            {
                if (_checkoutStatusMessages.TryGetValue(status, out string? result))
                {
                    return result;
                }
                throw new ArgumentException($"Unsupported checkout status: {status}");
            }
            private static string GetCheckInMessage(CheckInStatus status)
            {
                if (_checkInStatusMessages.TryGetValue(status, out string? result))
                {
                    return result;
                }
                throw new ArgumentException($"Unsupported check in status: {status}");
            }
            private static string GetListBooksMessage(ListBooksStatus status)
            {
                if (_listBooksStatusMessages.TryGetValue(status, out string? result))
                {
                    return result;
                }
                throw new ArgumentException($"Unsupported list books status: {status}");
            }
            private static string GetSearchBookMessage(SearchBookStatus status)
            {
                if (_searchBookStatusMessages.TryGetValue(status, out string? result))
                {
                    return result;
                }
                throw new ArgumentException($"Unsupported search book status: {status}");
            }
        }
    }

    #endregion |--- LibraryManager partial classes ---|
}



